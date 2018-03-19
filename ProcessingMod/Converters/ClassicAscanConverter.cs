using NCE.CommonData;
using NCE.ModulesCommonData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using ZedGraph;

namespace NCE.Processing.Drawing
{
    /// <summary>
    /// Сырые данные а-сканов в точки отрисовки
    /// </summary>
    public class ClassicAscanConverter : IPropagatorBlock<byte[], List<Channel>>, IRawDataInputModule
    {
        /// <summary>
        /// Блок конвертации из сырых данных в точки отрисовки
        /// </summary>
        private readonly TransformManyBlock<byte[], List<Channel>> _converterBlock;
        /// <summary>
        /// DataType менеджер
        /// </summary>
        private readonly DataTypeManager _dataStructManager;
        /// <summary>
        /// Функция конвертации
        /// </summary>
        private Func<byte[], IEnumerable<List<Channel>>> convert;
        /// <summary>
        /// Шаг сканирования
        /// </summary>
        private double _multiplier;
        /// <summary>
        /// Список офсетов, ключь Id
        /// </summary>
        private Dictionary<int, double> _channelsStartOffset = new Dictionary<int, double>();
        /// <summary>
        /// Временный список конвертированных данных
        /// </summary>
        private Dictionary<int, Channel> _parsedChannels = new Dictionary<int, Channel>();
        /// <summary>
        /// Временный список конвертированных данных для суммирования
        /// </summary>
        private Dictionary<int, List<PointPair>> _parsedSummChannels = new Dictionary<int, List<PointPair>>();
        private AscanConverterWorkingMod _mod;
        private int _refreshCount;
        private int _currentStep = 1;

        public Task Completion { get { return _converterBlock.Completion; } }
        public string ModuleName {  get { return "ClassicAscanConverter"; } }

        public void Complete()
        {
            _converterBlock.Complete();
        }

        public ClassicAscanConverter(DataTypeManager dataStructManager, Dictionary<int, double> channelsStartOffset, double multiplier, int refreshCount, AscanConverterWorkingMod mod)
        {
            if (refreshCount < 1)
                throw new ArgumentOutOfRangeException("refreshCount", "Refresh count < 1!");

            _multiplier = multiplier;
            _dataStructManager = dataStructManager;
            convert = PointsConverter;
            _mod = mod;
            _channelsStartOffset = channelsStartOffset;
            _refreshCount = refreshCount;
            _converterBlock = new TransformManyBlock<byte[], List<Channel>>(convert, new ExecutionDataflowBlockOptions() { SingleProducerConstrained = true, MaxDegreeOfParallelism = 1 });
        }

        #region DataFlow
        public List<Channel> ConsumeMessage(DataflowMessageHeader messageHeader, ITargetBlock<List<Channel>> target, out bool messageConsumed)
        {
            return ((IPropagatorBlock<byte[], List<Channel>>)_converterBlock).ConsumeMessage(messageHeader, target, out messageConsumed);
        }

        public void Fault(Exception exception)
        {
            ((IDataflowBlock)_converterBlock).Fault(exception);
        }

        public IDisposable LinkTo(ITargetBlock<List<Channel>> target, DataflowLinkOptions linkOptions)
        {
            return _converterBlock.LinkTo(target, linkOptions);
        }

        public DataflowMessageStatus OfferMessage(DataflowMessageHeader messageHeader, byte[] messageValue, ISourceBlock<byte[]> source, bool consumeToAccept)
        {
            return ((IPropagatorBlock<byte[], List<Channel>>)_converterBlock).OfferMessage(messageHeader, messageValue, source, consumeToAccept);
        }

        public void ReleaseReservation(DataflowMessageHeader messageHeader, ITargetBlock<List<Channel>> target)
        {
            ((IPropagatorBlock<byte[], List<Channel>>)_converterBlock).ReleaseReservation(messageHeader, target);
        }

        public bool ReserveMessage(DataflowMessageHeader messageHeader, ITargetBlock<List<Channel>> target)
        {
            return ((IPropagatorBlock<byte[], List<Channel>>)_converterBlock).ReserveMessage(messageHeader, target);
        }
        #endregion
        public void PostData(byte[] raw)
        {
            _converterBlock.Post(raw);
        }

        /// <summary>
        /// Функция конвертации сырых данных в точки отрисовки
        /// </summary>
        /// <param name="data">Сырые данные, структура как в DataType</param>
        /// <returns>Точки для отрисовки</returns>
        private IEnumerable<List<Channel>> PointsConverter(byte[] data)
        {
            _parsedChannels.Clear();
            for (int i = 0; i < data.Length / _dataStructManager.FrameSize; i++)
            {
                int id = data[i * _dataStructManager.FrameSize];
                double xPoint = BitConverter.ToUInt32(data, i * _dataStructManager.FrameSize + _dataStructManager.PointXOffset) * _multiplier - _channelsStartOffset[id];
                if (xPoint > 0)
                {
                    if (!_parsedChannels.ContainsKey(id))
                    {
                        _parsedChannels[id] = new Channel(1);
                    }


                    List<PointPair> singleParsed;// = new List<PointPair>();
                    if (_mod == AscanConverterWorkingMod.Snapshot)
                    {
                        if (_currentStep % _refreshCount == 0)
                        {
                            singleParsed = ParseAscan(data, i * _dataStructManager.FrameSize, _dataStructManager);
                            _parsedChannels[id].Gates[0].GatePoints = singleParsed;

                        }
                    }
                    else if (_mod == AscanConverterWorkingMod.SummCollecting)
                    {
                        singleParsed = ParseAscan(data, i * _dataStructManager.FrameSize, _dataStructManager);
                        if (!_parsedSummChannels.ContainsKey(id))
                        {
                            _parsedSummChannels.Add(id, singleParsed);
                        }
                        else
                        {
                            SummPoints(_parsedSummChannels[id], singleParsed);
                        }

                        if (_currentStep % _refreshCount == 0)
                        {
                            _parsedChannels[id].Gates[0].GatePoints = _parsedSummChannels[id].ToList();
                            _parsedSummChannels.Remove(id);
                        }
                    }
                    _currentStep++;
                }
            }
            return new List<List<Channel>>() { _parsedChannels.Values.ToList() };
        }
        /// <summary>
        /// Парс точек а-скана
        /// </summary>
        /// <param name="rawArray">Сырые данные, структура как в DataType</param>
        /// <param name="coordinateOffset">Офсет начала фрейма</param>
        /// <param name="manager">DataType менеджер</param>
        /// <returns>Список координат по гейтам</returns>
        private List<PointPair> ParseAscan(byte[] rawArray, int coordinateOffset, DataTypeManager manager)
        {
            List<PointPair> res = new List<PointPair>(manager.GateAmpsOffset.Count);
            int id = rawArray[coordinateOffset];
            //double x = BitConverter.ToUInt32(rawArray, manager.PointXOffset + coordinateOffset) * _multiplier;// - _channelsStartOffset[id];

            int idx = 0;
            int scansMaxCount = BitConverter.ToUInt16(rawArray, coordinateOffset + manager.AscanPointCount) - 1;
            int startTime = manager.GetAscanStartTime(rawArray, coordinateOffset);
            int endTime = manager.GetAscanEndTime(rawArray, coordinateOffset);
            foreach (var point in manager.AscanIterator(rawArray, coordinateOffset))
            {
                double x = (startTime + (double)(idx * (endTime - startTime)) / scansMaxCount) / 1000;
                res.Add(new PointPair(x, point));
                idx++;
            }
            return res;
        }

        private void SummPoints(List<PointPair> tempSumm, List<PointPair> parsedChannel)
        {
            for (int i = 0; i < tempSumm.Count; i++)
            {
                tempSumm[i].Y += parsedChannel[i].Y;
                if (tempSumm[i].Y > 100)
                    tempSumm[i].Y = 100;
            }
        }
    }
}
