using NCE.CommonData;
using NCE.ModulesCommonData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace NCE.Processing
{
    /// <summary>
    /// Сплит модуль, рассылает входящие данные всем подписавшимся модулям
    /// </summary>
    public class DataSplitter<T> : IRawSplitterTarget, IReceivableSourceBlock<byte[]>, ITargetBlock<byte[]>, IModule where T : IModule, IRawSplitterTarget
    {

        private double _barrierCoord = double.MaxValue;
        /// <summary>
        /// DataType менеджер
        /// </summary>
        private DataTypeManager _manager;
        /// <summary>
        /// Шаг сканирования
        /// </summary>
        private double _multiplier;
        /// <summary>
        /// Внутренний буфер
        /// </summary>
        private BufferBlock<byte[]> _innerBuffer = new BufferBlock<byte[]>();
        /// <summary>
        /// Список офсетов, ключь - ID канала
        /// </summary>
        private Dictionary<int, double> _channelsStartOffset;
        /// <summary>
        /// Сплит функция
        /// </summary>
        private ActionBlock<byte[]> _splitterBlock;
        /// <summary>
        /// Список подписавшихся модулей для получения сырых данных
        /// </summary>
        private List<T> _dataTarget = new List<T>();
        /// <summary>
        /// Список подписавшихся модулей для получения флага сохраненния координаты лайт барьера
        /// </summary>
        private List<ILightBarrierSplitterTarget> _lightBarrierTarget = new List<ILightBarrierSplitterTarget>();
        /// <summary>
        /// Флаг для сохранненния координаты
        /// </summary>
        private bool _lightBarrierReached = false;
        /// <summary>
        /// Метка о сохранении барьера
        /// </summary>
        private bool _lightBarriedSaved = false;
        ///// <summary>
        ///// Офсет датчика для канала в мм
        ///// </summary>
        //private Dictionary<int, double> _channelProbeOffset;
        /// <summary>
        /// Время начала контроля (первая точка отрисовки)
        /// </summary>
        private DateTime _startTime;
        ///// <summary>
        ///// Время окнца контроля (лайт барьер)
        ///// </summary>
        //private DateTime _endTime;
        private bool _startTimeSaved = false;
        //private bool _endTimeSaved = false;
        private Action<string> _logingAct = (string s) => { };

        public double LightBarrierCoord
        {
            get
            {
                return _barrierCoord;
            }
        }

        public string ModuleName
        {
            get
            {
                return "Splitter";
            }
        }

        public DateTime StartTime
        { get { return _startTime; } }

        //public DateTime EndTime
        //{ get { return _endTime; } }

        /// <summary>
        /// Инициализация модуля
        /// </summary>
        public DataSplitter(DataTypeManager manager, double multiplier, Dictionary<int, double> channelsStartOffset, Action<string> logingAct)//, List<Offset> probeOffset)
        {
            _logingAct = logingAct;
               _manager = manager;
            _multiplier = multiplier;
            _channelsStartOffset = channelsStartOffset;
            _splitterBlock = new ActionBlock<byte[]>(new Action<byte[]>(Split), new ExecutionDataflowBlockOptions() { MaxDegreeOfParallelism = 1, });
            //_channelProbeOffset = new Dictionary<int, double>();
            //foreach (var offset in probeOffset)
            //{
            //    _channelProbeOffset.Add(offset.BoardId * 16 + offset.ChannelId, offset.Mm);
            //}
            //Передача завершения сбора всем подписанным модулям
            _splitterBlock.Completion.ContinueWith(t =>
            {
                OnComplete();
            }
            );
            //PropagateCompletion - обязательный, автоматическая передача завершения контроля
            _innerBuffer.LinkTo(_splitterBlock, new DataflowLinkOptions() { PropagateCompletion = true });
        }
        /// <summary>
        /// Регистрация модуля для получения сырых данных
        /// </summary>
        /// <param name="newTarget">Целевой модуль</param>
        public void RegisterRawDataSplitterTarget(T newTarget)
        {
            if (newTarget == null)
                throw new ArgumentNullException("newTarget", "Splitter target can't be null!");
            _dataTarget.Add(newTarget);
        }
        /// <summary>
        /// Регистрация модуля для получения флага сохраненния координаты лайт барьера
        /// </summary>
        /// <param name="newTarget"></param>
        public void RegisterLightBarrierSplitterTarget(ILightBarrierSplitterTarget newTarget)
        {
            if (newTarget == null)
                throw new ArgumentNullException("newTarget", "Splitter target can't be null!");
            _lightBarrierTarget.Add(newTarget);
        }

        /// <summary>
        /// Добавленние данных на рассылку без использования DataFlow модулей(либо другим сплитером)
        /// </summary>
        /// <param name="rawData"></param>
        public void PostData(byte[] rawData)
        {
            if (_lightBarrierReached && !_lightBarriedSaved)
            {
                _barrierCoord = ParseCoord(rawData);

                SendBarrierCoord();

                _lightBarriedSaved = true;
            }
            if (!_startTimeSaved)
            {
                _startTime = DateTime.Now;
                _startTimeSaved = true;
            }

            _innerBuffer.Post(rawData);
        }

        /// <summary>
        /// Установка флага лайт барьера для сохраненния координаты
        /// </summary>
        public void LightBarrierReached()
        {
            _lightBarrierReached = true;
        }
        /// <summary>
        /// Установка флага лайт барьера для сохраненния координаты
        /// </summary>
        public void SetLightBarrierCoord(double coord)
        {
            _barrierCoord = coord;
            _lightBarriedSaved = true;
            SendBarrierCoord();
        }

        #region DataFlow block
        public Task Completion => _splitterBlock.Completion;

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
        /// Передача данных подписанным модулям
        /// </summary>
        /// <param name="channels">Сырые данные</param>
        private void Split(byte[] channels)
        {
            var tempList = _dataTarget.ToList();

            foreach (var target in tempList)
            {
                target.PostData(channels);
            }
        }

        private void SendBarrierCoord()
        {
            var tempList = _lightBarrierTarget.ToList();

            foreach (var target in tempList)
            {
                target.LightBarrierReached(_barrierCoord);
            }
        }

        private double ParseCoord(byte[] rawData)
        {
            return BitConverter.ToUInt32(rawData, _manager.FrameSize + _manager.PointXOffset) * _multiplier - _channelsStartOffset[0];
        }

        /// <summary>
        /// Передача завершения контроля подписаным модулям
        /// </summary>
        private void OnComplete()
        {
            var tempDataList = _dataTarget.ToList();
            foreach (var target in tempDataList)
            {
                try
                {
                    target.Complete();
                    target.Completion.Wait();//Ожидание завершения работы модуля
                }
                catch (AggregateException ex)
                {
                    foreach (var innerEx in ex.InnerExceptions)
                    {
                        _logingAct(innerEx.Message);
                    }
                }
                catch (Exception ex)
                {
                    _logingAct(ex.Message);
                }
            }
            var tempLightBarrierList = _lightBarrierTarget.ToList();
            foreach (var target in tempLightBarrierList)
            {
                target.Complete();
                target.Completion.Wait();//Ожидание завершения работы модуля
             }
        }
    }
}
