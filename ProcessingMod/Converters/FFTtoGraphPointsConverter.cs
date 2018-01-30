using NCE.CommonData;
using NCE.ModulesCommonData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using ZedGraph;

namespace NCE.Processing.Converters
{
    public class FFTtoGraphPointsConverter : IPropagatorBlock<ConvertedFFT, List<Channel>>, IFFTConvertedInputModule
    {  
        /// <summary>
        /// Блок конвертации из сырых данных в точки отрисовки
        /// </summary>
        private readonly TransformBlock<ConvertedFFT, List<Channel>> _converterBlock;
        /// <summary>
        /// DataType менеджер
        /// </summary>
        private readonly DataTypeManager _dataStructManager;
        /// <summary>
        /// Функция конвертации
        /// </summary>
        private Func<ConvertedFFT, List<Channel>> convert;
        private Dictionary<int, Channel> _tempDict = new Dictionary<int, Channel>();


        public Task Completion
        {
            get { return _converterBlock.Completion; }
        }

        public string ModuleName
        { get { return "FFTtoGraphPointsConverter"; } }
        public void Complete()
        {
            _converterBlock.Complete();
        }

        public FFTtoGraphPointsConverter(DataTypeManager dataStructManager)
        {
            _dataStructManager = dataStructManager;
            convert = PointsConverter;
            _converterBlock = new TransformBlock<ConvertedFFT, List<Channel>>(convert, new ExecutionDataflowBlockOptions() { SingleProducerConstrained = true, MaxDegreeOfParallelism = 1 });
        }

        #region DataFlow
        public List<Channel> ConsumeMessage(DataflowMessageHeader messageHeader, ITargetBlock<List<Channel>> target, out bool messageConsumed)
        {
            return ((IPropagatorBlock<ConvertedFFT, List<Channel>>)_converterBlock).ConsumeMessage(messageHeader, target, out messageConsumed);
        }

        public void Fault(Exception exception)
        {
            ((IPropagatorBlock<ConvertedFFT, List<Channel>>)_converterBlock).Fault(exception);
        }

        public IDisposable LinkTo(ITargetBlock<List<Channel>> target, DataflowLinkOptions linkOptions)
        {
            return _converterBlock.LinkTo(target, linkOptions);
        }

        public DataflowMessageStatus OfferMessage(DataflowMessageHeader messageHeader, ConvertedFFT messageValue, ISourceBlock<ConvertedFFT> source, bool consumeToAccept)
        {
            return ((IPropagatorBlock<ConvertedFFT, List<Channel>>)_converterBlock).OfferMessage(messageHeader, messageValue, source, consumeToAccept);
        }

        public void ReleaseReservation(DataflowMessageHeader messageHeader, ITargetBlock<List<Channel>> target)
        {
            ((IPropagatorBlock<ConvertedFFT, List<Channel>>)_converterBlock).ReleaseReservation(messageHeader, target);
        }

        public bool ReserveMessage(DataflowMessageHeader messageHeader, ITargetBlock<List<Channel>> target)
        {
            return ((IPropagatorBlock<ConvertedFFT, List<Channel>>)_converterBlock).ReserveMessage(messageHeader, target);
        }
        #endregion

        public void PostData(ConvertedFFT data)
        {
            _converterBlock.Post(data);
        }

        private List<Channel> PointsConverter(ConvertedFFT data)
        {
            _tempDict.Clear();
            int spectrIdx = 0;
            foreach (var spect in data.Spectr)
            {
                if (!_tempDict.ContainsKey(spect.ChannelId))
                {
                    Channel ch = new Channel(1, spect.SpectrFFTData.Length);
                    ch.ChannelId = spect.ChannelId;
                    _tempDict[spect.ChannelId] = ch;
                }
                int idx = 0;
                double AscanStart = _dataStructManager.GetAscanStartTime(data.RawData, _dataStructManager.FrameSize * spectrIdx);
                double AscanEnd = _dataStructManager.GetAscanEndTime(data.RawData, _dataStructManager.FrameSize * spectrIdx);
                double StepA = (AscanEnd - AscanStart) / (BitConverter.ToUInt16(data.RawData, spectrIdx + _dataStructManager.AscanPointCountOffset) - 1) / 1000;
                double StepF = 1 / StepA / (data.Spectr[0].SpectrFFTData.Length * 2);

                foreach (var point in spect.SpectrFFTData)
                {
                    _tempDict[spect.ChannelId].Gates[0].GatePoints.Add(new PointPair(AscanStart + StepF * idx, point.Real * point.Real + point.Imaginary * point.Imaginary));
                    idx++;
                }
                spectrIdx++;
            }
            return _tempDict.Values.ToList();
        }
    }
}
