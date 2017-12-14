using NCE.CommonData;
using NCE.ModulesCommonData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Windows.Forms;

namespace NCE.Processing
{
    public class LinearDefCalc : ILightBarrierSplitterTarget, IReceivableSourceBlock<byte[]>, ITargetBlock<byte[]>, IRawDataInputModule
    {
        //public const string DefSplitChar = "#";
        //public const string DefInnerSplitChar = "@";

        /// <summary>
        /// Внутренний буфер
        /// </summary>
        private BufferBlock<byte[]> _innerBuffer = new BufferBlock<byte[]>();
        /// <summary>
        /// Блок подсчета дефектов
        /// </summary>
        private ActionBlock<byte[]> _defCalcBlock;
        /// <summary>
        /// DataType менеджер
        /// </summary>
        private DataTypeManager _manager;
        /// <summary>
        /// Грид записи дефеков
        /// </summary>
        //private DataGridView _dgView;
        /// <summary>
        /// Список дефектов, ключь - ID канала и строб по формуле(см. код)
        /// </summary>
        private Dictionary<int, Defect> _defects = new Dictionary<int, Defect>();
        /// <summary>
        /// Список офсетов, ключь - ID канала
        /// </summary>
        private Dictionary<int, double> _channelsStartOffset;
        /// <summary>
        /// Список офсетов, ключь - ID канала
        /// </summary>
        private /*Dictionary<int, double>*/double _channelsDeadZoneStartOffset;
        /// <summary>
        /// Список офсетов, ключь - ID канала
        /// </summary>
        private /*Dictionary<int, double>*/double _channelsDeadZoneEndOffset;
        /// <summary>
        /// Настройки фильтрации дефектов
        /// </summary>
        private LinearDefCalcSettings _defCaclSettings;
        /// <summary>
        /// Офсет датчика для канала в мм
        /// </summary>
        //private Dictionary<int, double> _channelProbeOffset;
        /// <summary>
        /// Координата лайт барьера, дальше дефекты не считаются
        /// </summary>
        private double _stopCoord = 9999999;
        /// <summary>
        /// Флаг для сохранения координаты лайт барьера
        /// </summary>
        //private bool _saveStopCoord = false;
        /// <summary>
        /// Флаг для игнорирования повторных попыток сохранить лайт барьер
        /// </summary>
        //private bool _stopCoordSaved = false;
        /// <summary>
        /// Шаг сканирования
        /// </summary>
        private double _multiplier;
        /// <summary>
        /// Количество записаных дефектов
        ///// </summary>
        //private int _defectsAddedToDgv = 1;
        /// <summary>
        /// Датчик проехал координату лайтбарьера
        /// </summary>
        private Dictionary<int, bool> _barrierReachedFlag = new Dictionary<int, bool>();
        /// <summary>
        /// Для создания строки дефектов
        ///// </summary>
        //private StringBuilder _sb = new StringBuilder();
        /// Для создания строки дефектов
        ///// </summary>
        //private bool _sbDone = false;
        /// <summary>
        /// Список дефектов
        /// </summary>
        private List<Defect> _defectsList = new List<Defect>(32);


        /// <summary>
        /// Датчик проехал координату лайтбарьера
        /// </summary>
        public Dictionary<int, bool> BarrierReached
        {
            get { return _barrierReachedFlag; }
        }
        /// <summary>
        /// Списрк дефектов
        /// </summary>
        public List<Defect> DefectList
        {
            get
            {
                return _defectsList;
            }
        }
        public string ModuleName
        {
            get
            {
                return "DefCalc";
            }
        }

        /// <summary>
        /// Реализация линейного подсчета дефектов
        /// </summary>
        /// <param name="dgView">Грид для сохранения дефектов, колонки должны быть созданы до начала подсчета дефектов</param>
        /// <param name="manager">DataType менеджер</param>
        /// <param name="defCaclSettings">Настройки фильтрации дефектов</param>
        /// <param name="channelsStartOffset">Список офсетов</param>
        /// <param name="channelsDeadZoneStartOffset">Список офсетов</param>
        /// <param name="channelsDeadZoneEndOffset">Список офсетов</param>
        /// <param name="multiplier">Шаг сканирования</param>
        public LinearDefCalc(DataTypeManager manager, /*LinearDefCalcSettings*/ int defCalcSettings, Dictionary<int, double> channelsStartOffset,
            /*Dictionary<int, double>*/ double channelsDeadZoneStartOffset, /*Dictionary<int, double>*/ double channelsDeadZoneEndOffset, double multiplier)//, List<Offset> probeOffset)
        {
            if (manager.DetOffset == -1)
                throw new ArgumentOutOfRangeException("Det is not present in DataType!");

            _multiplier = multiplier;
            //_dgView = dgView;
            //_dgView.Rows.Clear();
            _manager = manager;
            //_defCaclSettings = defCaclSettings;
            _defCaclSettings = new LinearDefCalcSettings(defCalcSettings, 0);
            _channelsStartOffset = channelsStartOffset;
            _channelsDeadZoneStartOffset = channelsDeadZoneStartOffset;
            _channelsDeadZoneEndOffset = channelsDeadZoneEndOffset;
            _defCalcBlock = new ActionBlock<byte[]>(new Action<byte[]>(CalcDef), new ExecutionDataflowBlockOptions() { MaxDegreeOfParallelism = 1 });//com maxDeg
            //_channelProbeOffset = new Dictionary<int, double>();
            //foreach (var offset in probeOffset)
            //{
            //    _channelProbeOffset.Add(offset.BoardId * 16 + offset.ChannelId, offset.Mm);
            //}
            //Если есть дефект который незаписали еще в грид и мы закончили сканирование - нужно записать его
            //_defCalcBlock.Completion.ContinueWith(
            //    (FinishDefAdd) =>
            //    {
            //        AddDefOnComplete();
            //    }
            //    );
            //PropagateCompletion - обязателен, авто передача завершения работы модулю подсчета.
            _innerBuffer.LinkTo(_defCalcBlock, new DataflowLinkOptions() { PropagateCompletion = true });

        }

        #region DataFlow block
        public Task Completion => _defCalcBlock.Completion;

        public void Complete()
        {
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

        public void PostData(byte[] raw)
        {
            _innerBuffer.Post(raw);
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
        /// Функция установки флага для сохранения лайт барьера
        /// </summary>
        public void LightBarrierReached(double barrierCoord)
        {
            //_saveStopCoord = true;
            _stopCoord = barrierCoord;
        }
        
        /// <summary>
        /// Функция подсчета дефектов
        /// </summary>
        /// <param name="rawData">Массив сырых данных, структура должны совпадать с DataType</param>
        private void CalcDef(byte[] rawData)
        {
            ////Сохранение координаты лайт барьера
            //if (_saveStopCoord && !_stopCoordSaved)
            //{
            //    _stopCoord = BitConverter.ToUInt32(rawData, _manager.FrameSize + _manager.PointXOffset) * _multiplier - _channelsStartOffset[0];
            //    _stopCoordSaved = true;
            //}
            for (int i = 0; i < rawData.Length / _manager.FrameSize; i++)
            {
                bool detectedAny = _manager.IsAnyDetected(rawData, i * _manager.FrameSize + _manager.DetOffset);
                byte id = rawData[i * _manager.FrameSize];
                //double xPoint = BitConverter.ToUInt32(rawData, i * _manager.FrameSize + _manager.PointXOffset) - _channelsStartOffset[id];
                double xPoint = BitConverter.ToUInt32(rawData, i * _manager.FrameSize + _manager.PointXOffset) * _multiplier - _channelsStartOffset[id];
                if (detectedAny && xPoint > _channelsDeadZoneStartOffset && xPoint < _stopCoord - _channelsDeadZoneEndOffset)
                {
                    int gateIdx = 0;
                    foreach (var gateDetected in _manager.GateDetIterator(rawData[i * _manager.FrameSize + _manager.DetOffset]))
                    {
                        //Ключь дефекта в Dictionary, в Id board при 2 и более компах должны иметь сквозное нумерование (1 комп 0-16, 2 комп 17-32...)
                        int defKey = gateIdx << 8 + id;
                        //Начало\продолжение дефекта
                        if (gateDetected)
                        {
                            //Новый дефект
                            if (!_defects.ContainsKey(defKey))
                            {
                                var tempDef = new Defect(_manager.AllGateNames[gateIdx], id, xPoint, _multiplier);
                                _defects.Add(defKey, tempDef);
                            }
                            else
                            {
                                _defects[defKey].Lenght += _multiplier;
                            }
                        }
                        else//Конец дефекта
                        {
                            if (_defects.ContainsKey(defKey))
                            {
                                if (_defCaclSettings.MinDefectLenght < _defects[defKey].Lenght)
                                {
                                    //AddDataToDgv(_defects[defKey]);
                                    var def = _defects[defKey];
                                    _defectsList.Add(def);
                                    //_defectsList.Add(string.Join(DefInnerSplitChar, new string[]{(_defectsList.Count + 1).ToString(),
                                    //    _manager.GetBoard(def.ChannelId).ToString(), _manager.GetChannel(def.ChannelId).ToString(),
                                    //    def.GateName, def.StartPoint.ToString("N0"), def.Lenght.ToString("N0")}));
                                }
                            _defects.Remove(defKey);
                            }
                        }
                        gateIdx++;
                    }
                }
                else//Закрытие дефектов при достижении лайт барьера, наверно?
                {
                    int gateIdx = 0;
                    foreach (var gateDetected in _manager.GateDetIterator(rawData[i * _manager.FrameSize + _manager.DetOffset]))
                    {
                        int defKey = gateIdx << 8 + id;
                        if (_defects.ContainsKey(defKey))
                        {
                            if (_defCaclSettings.MinDefectLenght < _defects[defKey].Lenght)
                            {
                                //AddDataToDgv(_defects[defKey]);
                                var def = _defects[defKey];
                                _defectsList.Add(def);
                                //_defectsList.Add(string.Join(DefInnerSplitChar, new string[]{(_defectsList.Count + 1).ToString(),
                                //    _manager.GetBoard(def.ChannelId).ToString(), _manager.GetChannel(def.ChannelId).ToString(),
                                //    def.GateName, def.StartPoint.ToString("N0"), def.Lenght.ToString("N0")}));
                            }
                            _defects.Remove(defKey);
                        }
                        gateIdx++;
                    }
                    if (xPoint > _stopCoord - _channelsDeadZoneEndOffset + _channelsStartOffset[id])
                    { _barrierReachedFlag[id] = true; }
                }
            }

        }
        ///// <summary>
        ///// Запись дефектов в грид
        ///// </summary>
        ///// <param name="def">Дефект</param>
        //private void AddDataToDgv(Defect def)
        //{
        //    var idx = _defectsAddedToDgv++;
        //    _dgView.Invoke((MethodInvoker)delegate
        //    {
        //        _dgView.Rows.Add(new object[] { idx, _manager.GetBoard(def.DataTypeId), _manager.GetChannel(def.DataTypeId), def.GateName, (int)def.StartPoint, (int)def.Lenght });
        //    });
        //}

        ///// <summary>
        ///// Запись дефектов в грид по завершению сбора
        ///// </summary>
        //private void AddDefOnComplete()
        //{
        //    foreach (var def in _defects.Values)
        //    {
        //        AddDataToDgv(def);
        //    }
        //}
    }
    /// <summary>
    /// Клас для описания дефекта
    /// </summary>
    public class Defect
    {
        public string GateName { get; }
        /// <summary>
        /// Id - board\channel
        /// </summary>
        public byte ChannelId { get; }
        public double StartPoint { get; }
        public double Lenght { get; set; }
        public double MaxAmp { get; set; }

        public Defect(string gateName, byte channelId, double startPoint, double lenght = 1, double maxAmp = 0)
        {
            GateName = gateName;
            ChannelId = channelId;
            StartPoint = startPoint;
            Lenght = lenght;
            MaxAmp = maxAmp;
        }        
    }
}
