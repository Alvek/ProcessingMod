using NCE.CommonData;
using NCE.UTscanner.ModulesCommonData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace NCE.UTscanner.Processing
{
    /// <summary>
    /// Модуль краскоотметки
    /// </summary>
    public class DefPainter : ILightBarrierSplitterTarget, IReceivableSourceBlock<byte[]>, ITargetBlock<byte[]>, IRawDataInputModule
    {
        /// <summary>
        /// Внутренний буфер
        /// </summary>
        private BufferBlock<byte[]> _innerBuffer = new BufferBlock<byte[]>();
        /// <summary>
        /// Функция покраски
        /// </summary>
        private ActionBlock<byte[]> _painterBlock;
        /// <summary>
        /// Флаги для краскоотметки, ключь - id, value - массив флагов по гейтам. 1 - есть дефект, 0 - нет дефекта
        /// </summary>
        private Dictionary<int, int[]> _flags = new Dictionary<int, int[]>();
        /// <summary>
        /// Отсечка по длине дефекта
        /// </summary>
        private Dictionary<int, List<StartStopCoord>[]> _startStopCoord = new Dictionary<int, List<StartStopCoord>[]>();
        /// <summary>
        /// Список дефектов, ключь - id, value список дефектов по гейтам
        /// </summary>
        private Dictionary<int, List<Queue<double>>> _paintCoords = new Dictionary<int, List<Queue<double>>>();
        /// <summary>
        /// Менеджер
        /// </summary>
        private DataTypeManager _manager;
        /// <summary>
        /// Задержка (растояние между датчиком и лайт барьером)
        /// </summary>
        private int _delay = 0;
        /// <summary>
        /// Шаг контроля
        /// </summary>
        private double _multiplier;
        /// <summary>
        /// Список офсетов
        /// </summary>
        private Dictionary<int, double> _channelsStartOffset;
        /// <summary>
        /// Список офсетов
        /// </summary>
        private /*Dictionary<int, double>*/double _channelsDeadZoneStartOffset;
        /// <summary>
        /// Список офсетов
        /// </summary>
        private /*Dictionary<int, double>*/double _channelsDeadZoneEndOffset;
        /// <summary>
        /// Офсет датчика для канала в мм
        /// </summary>
        //private Dictionary<int, double> _channelProbeOffset;
        /// <summary>
        /// Координата лайт барьера
        /// </summary>
        private double _stopCoord = 9999999;
        /// <summary>
        /// Флаг для сохраненния координаты лайт барьера
        /// </summary>
        //private bool _saveStopCoord = false;
        /// <summary>
        /// Флаг для игнорирования повторных попыток сохранить лайт барьрер
        /// </summary>
        //private bool _stopCoordSaved = false;
        /// <summary>
        /// Флаг для сброса флагов дефектов снаружи
        /// </summary>
        private bool _clearFlag = false;
        /// <summary>
        /// Настройки фильтрации дефектов
        /// </summary>
        private LinearDefCalcSettings _defCaclSettings;
        /// <summary>
        /// Краскоотметчик проехал точку лайтбарьера
        /// </summary>
        private Dictionary<int, bool> _barrierReachedFlag = new Dictionary<int, bool>();

        public string ModuleName
        {
            get
            {
                return "Painter";
            }
        }

        /// <summary>
        /// Краскоотметчик проехал точку лайтбарьера
        /// </summary>
        public Dictionary<int, bool> BarrierReached
        {
            get { return _barrierReachedFlag; }
        }
        /// <summary>
        /// Флаги для краскоотметки, ключ - id, value - массив флагов по гейтам. 1 - есть дефект, 0 - нет дефекта
        /// </summary>
        public IReadOnlyDictionary<int, int[]> Flags
        {
            get
            {
                return _flags;
            }
        }
        /// <summary>
        /// Инициализация модуля
        /// </summary>
        /// <param name="coordinateDelay">Задержка, растояние между датчиком и лайт барьером</param>
        /// <param name="manager">Менеджер</param>
        /// <param name="channelsStartOffset">Список офсетов</param>
        /// <param name="channelsDeadZoneStartOffset">Список офсетов</param>
        /// <param name="channelsDeadZoneEndOffset">Список офсетов</param>
        /// <param name="multiplier">Шаг контроля</param>
        public DefPainter(int coordinateDelay, DataTypeManager manager, /*LinearDefCalcSettings*/int defCalcSettings, Dictionary<int, double> channelsStartOffset,
            /*Dictionary<int, double>*/double channelsDeadZoneStartOffset, /*Dictionary<int, double>*/double channelsDeadZoneEndOffset, double multiplier)//, List<Offset> probeOffset)
        {
            if (manager.DetOffset == -1)
                throw new ArgumentOutOfRangeException("Det is not present in DataType!");

            //_defCaclSettings = defCaclSettings;
            _defCaclSettings = new LinearDefCalcSettings(defCalcSettings, 0);
            _multiplier = multiplier;
            _manager = manager;
            _delay = coordinateDelay;
            _channelsStartOffset = channelsStartOffset;
            _channelsDeadZoneStartOffset = channelsDeadZoneStartOffset;
            _channelsDeadZoneEndOffset = channelsDeadZoneEndOffset;
            _painterBlock = new ActionBlock<byte[]>(new Action<byte[]>(Paint), new ExecutionDataflowBlockOptions() { MaxDegreeOfParallelism = 1 });//com
            //_channelProbeOffset = new Dictionary<int, double>();
            //foreach (var offset in probeOffset)
            //{
            //    _channelProbeOffset.Add(offset.BoardId * 16 + offset.ChannelId, offset.Mm);
            //}
            //PropagateCompletion - обазательный, автоматическая передача окончания контроля
            _innerBuffer.LinkTo(_painterBlock, new DataflowLinkOptions() { PropagateCompletion = true });
        }


        #region DataFlow block
        public Task Completion => _painterBlock.Completion;

        public void Complete()
        {
            //TODO на завершении нужно выставить все флаги в 0
            _innerBuffer.Complete();
        }

        public byte[] ConsumeMessage(DataflowMessageHeader messageHeader, ITargetBlock<byte[]> target, out bool messageConsumed)
        {
            return ((IReceivableSourceBlock<byte[]>)_innerBuffer).ConsumeMessage(messageHeader, target, out messageConsumed);
        }

        public void Fault(Exception exception)
        {
            ((IReceivableSourceBlock<byte[]>)_innerBuffer).Fault(exception);
        }

        public IDisposable LinkTo(ITargetBlock<byte[]> target, DataflowLinkOptions linkOptions)
        {
            return _innerBuffer.LinkTo(target, linkOptions);
        }

        public DataflowMessageStatus OfferMessage(DataflowMessageHeader messageHeader, byte[] messageValue, ISourceBlock<byte[]> source, bool consumeToAccept)
        {
            return ((ITargetBlock<byte[]>)_innerBuffer).OfferMessage(messageHeader, messageValue, source, consumeToAccept);
        }

        public void PostData(byte[] rawData)
        {
            _innerBuffer.Post(rawData);
        }

        public void ReleaseReservation(DataflowMessageHeader messageHeader, ITargetBlock<byte[]> target)
        {
            ((IReceivableSourceBlock<byte[]>)_innerBuffer).ReleaseReservation(messageHeader, target);
        }

        public bool ReserveMessage(DataflowMessageHeader messageHeader, ITargetBlock<byte[]> target)
        {
            return ((IReceivableSourceBlock<byte[]>)_innerBuffer).ReserveMessage(messageHeader, target);
        }

        public bool TryReceive(Predicate<byte[]> filter, out byte[] item)
        {
            return _innerBuffer.TryReceive(filter, out item);
        }

        public bool TryReceiveAll(out IList<byte[]> items)
        {
            return _innerBuffer.TryReceiveAll(out items);
        }
        #endregion
        /// <summary>
        /// Установка флага для сохраненния координаты лайт барьера
        /// </summary>
        public void LightBarrierReached(double barrierCoord)
        {
            //_saveStopCoord = true;
            _stopCoord = barrierCoord;
        }
        /// <summary>
        /// Очистка флагов краскоотметки
        /// </summary>
        public void ClearPaintFlags()
        {
            _clearFlag = true;
        }
        /// <summary>
        /// Функция краскоотметки
        /// </summary>
        /// <param name="rawData">Сырые данные</param>
        private void Paint(byte[] rawData)
        {
            //Сброс флагов
            if (_clearFlag)
            {
                _clearFlag = false;
                foreach (var flag in _flags)
                {
                    Array.Clear(flag.Value, 0, flag.Value.Length);
                }
            }
            ////Сохранение координаты лайт барьера
            //if (_saveStopCoord && !_stopCoordSaved)
            //{
            //    double xPoint = BitConverter.ToUInt32(rawData, _manager.FrameSize + _manager.PointXOffset) * _multiplier - _channelsStartOffset[0];//error line, stop coord not saved
            //    _stopCoordSaved = true;
            //}
            for (int i = 0; i < rawData.Length / _manager.FrameSize; i++)
            {
                int gateIdx = 0;
                int id = rawData[i * _manager.FrameSize];
                double xPoint = BitConverter.ToUInt32(rawData, i * _manager.FrameSize + _manager.PointXOffset) * _multiplier - _channelsStartOffset[id];
                if (xPoint > _channelsDeadZoneStartOffset && xPoint < _stopCoord - _channelsDeadZoneEndOffset)
                {
                    if (!_startStopCoord.ContainsKey(id))
                    {
                        var list = new List<StartStopCoord>[_manager.GateAmpsOffset.Count];
                        for (int j = 0; j < _manager.GateAmpsOffset.Count; j++)
                        {
                            list[j] = new List<StartStopCoord>();
                            list[j].Add(new StartStopCoord());
                        }
                        _startStopCoord.Add(id, list);

                    }
                    if (!_flags.ContainsKey(id))
                    {
                        _flags.Add(id, new int[_manager.GateAmpsOffset.Count]);
                    }
                    if (!_paintCoords.ContainsKey(id))
                    {
                        _paintCoords.Add(id, new List<Queue<double>>());
                        for (int gateCount = 0; gateCount < _manager.GateAmpsOffset.Count; gateCount++)
                            _paintCoords[id].Add(new Queue<double>());
                    }

                    foreach (var gateDetected in _manager.GateDetIterator(rawData[i * _manager.FrameSize + _manager.DetOffset]))
                    {
                        if (_paintCoords[id][gateIdx].Count > 0 && _paintCoords[id][gateIdx].Peek() == xPoint - _delay)
                        {
                            _paintCoords[id][gateIdx].Dequeue();
                            double lenght = _startStopCoord[id][gateIdx][0].StopCoord - _startStopCoord[id][gateIdx][0].StartCoord;
                            if (_defCaclSettings.MinDefectLenght < lenght)
                            {
                                _flags[id][gateIdx] = 1;
                            }
                        }
                        if (gateDetected)
                        {
                            _paintCoords[id][gateIdx].Enqueue(xPoint);
                            if (_startStopCoord[id][gateIdx][_startStopCoord[id][gateIdx].Count - 1].StartCoord == 0)//Запись начала дефекта
                            {
                                _startStopCoord[id][gateIdx][_startStopCoord[id][gateIdx].Count - 1].StartCoord = xPoint;
                                _startStopCoord[id][gateIdx][_startStopCoord[id][gateIdx].Count - 1].DefectStarted = true;
                            }
                            else
                                _startStopCoord[id][gateIdx][_startStopCoord[id][gateIdx].Count - 1].StopCoord = xPoint;//Поиск последней координаты дефекта
                        }
                        else
                        {
                            if (!_startStopCoord[id][gateIdx][_startStopCoord[id][gateIdx].Count - 1].DefectEndReached &&
                                _startStopCoord[id][gateIdx][_startStopCoord[id][gateIdx].Count - 1].DefectStarted)//Закрываем дефект и создаем новый неинициализированый
                            {
                                _startStopCoord[id][gateIdx][_startStopCoord[id][gateIdx].Count - 1].DefectEndReached = true;
                                _startStopCoord[id][gateIdx].Add(new StartStopCoord());
                            }

                        }
                        if (_startStopCoord[id][gateIdx][0].StopCoord < xPoint - _delay)//Удаление дефектов которые мы полностью пометили
                            _startStopCoord[id][gateIdx].RemoveAt(0);
                        gateIdx++;
                    }
                }
                else
                {
                    if (_paintCoords.ContainsKey(id))
                        _paintCoords.Remove(id);
                }
                if (xPoint > _stopCoord - _channelsDeadZoneEndOffset + _channelsStartOffset[id])
                { _barrierReachedFlag[id] = true; }
            }
        }
    }

    class StartStopCoord
    {
        private double _stopCoord = double.MaxValue;

        public double StartCoord { get; set; }
        public double StopCoord
        {
            get
            {
                return _stopCoord;
            }
            set
            {
                _stopCoord = value;
            }
        }
        public bool DefectEndReached { get; set; }
        public bool DefectStarted { get; set; }
    }
}
