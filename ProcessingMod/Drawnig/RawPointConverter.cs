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
    public class Converter : IPropagatorBlock<byte[], List<Channel>>, IRawDataInputModule
    {
        /// <summary>
        /// Блок конвертации из сырых данных в точки отрисовки
        /// </summary>
        private readonly TransformBlock<byte[], List<Channel>> _converterBlock;
        /// <summary>
        /// DataType менеджер
        /// </summary>
        private readonly DataTypeManager _dataStructManager;
        /// <summary>
        /// Функция конвертации
        /// </summary>
        private Func<byte[], List<Channel>> convert;
        /// <summary>
        /// Шаг сканирования
        /// </summary>
        private double _multiplier;
        /// <summary>
        /// Список офсетов, ключь Id
        /// </summary>
        private Dictionary<int, double> _channelsStartOffset;

        public double Multiplier
        {
            get
            {
                return _multiplier;
            }
            set
            {
                _multiplier = value;
            }
        }

        public Task Completion
        {
            get
            {
                return _converterBlock.Completion;
            }
        }

        public string ModuleName
        {
            get { return "Converter"; }
        }

        /// <summary>
        /// Клас конвертации сырых данных в точки отрисовки
        /// </summary>
        /// <param name="dataStructManager">DataType менеджер</param>
        /// <param name="multiplier">Шаг сканирования</param>
        /// <param name="channelsStartOffset">Список офсетов, ключь ID</param>
        public Converter(DataTypeManager dataStructManager, double multiplier, Dictionary<int, double> channelsStartOffset)
        {
            _channelsStartOffset = channelsStartOffset;
            _multiplier = multiplier;
            _dataStructManager = dataStructManager;
            convert = PointsConverter;
            _converterBlock = new TransformBlock<byte[], List<Channel>>(convert);
        }

        #region Public
        #region DataFlowBlock
        public DataflowMessageStatus OfferMessage(DataflowMessageHeader messageHeader, byte[] messageValue, ISourceBlock<byte[]> source, bool consumeToAccept)
        {
            return ((IPropagatorBlock<byte[], List<Channel>>)_converterBlock).OfferMessage(messageHeader, messageValue, source, consumeToAccept);
        }

        public IDisposable LinkTo(ITargetBlock<List<Channel>> target, DataflowLinkOptions linkOptions)
        {
            return _converterBlock.LinkTo(target, linkOptions);
        }

        public List<Channel> ConsumeMessage(DataflowMessageHeader messageHeader, ITargetBlock<List<Channel>> target, out bool messageConsumed)
        {
            return ((IPropagatorBlock<byte[], List<Channel>>)_converterBlock).ConsumeMessage(messageHeader, target, out messageConsumed);
        }

        public bool ReserveMessage(DataflowMessageHeader messageHeader, ITargetBlock<List<Channel>> target)
        {
            return ((IPropagatorBlock<byte[], List<Channel>>)_converterBlock).ReserveMessage(messageHeader, target);
        }

        public void ReleaseReservation(DataflowMessageHeader messageHeader, ITargetBlock<List<Channel>> target)
        {
            ((IPropagatorBlock<byte[], List<Channel>>)_converterBlock).ReleaseReservation(messageHeader, target);
        }

        public void Complete()
        {
            _converterBlock.Complete();
            Console.WriteLine(string.Format("{0}\n{1}", _converterBlock.InputCount, _converterBlock.OutputCount));
        }

        public void Fault(Exception exception)
        {
            ((IPropagatorBlock<byte[], List<Channel>>)_converterBlock).Fault(exception);
        }
        #endregion
        /// <summary>
        /// Добавление данных для конвертации без использования DataFlow модулей (либо сплитером)
        /// </summary>
        /// <param name="data">Сырые данные, структура как в DataType</param>
        public void PostData(byte[] data)
        {
            _converterBlock.Post(data);
        }
        #endregion
        #region Private
        /// <summary>
        /// Функция конвертации сырых данных в точки отрисовки
        /// </summary>
        /// <param name="data">Сырые данные, структура как в DataType</param>
        /// <returns>Точки для отрисовки</returns>
        private List<Channel> PointsConverter(byte[] data)
        {
            Dictionary<int, Channel> parsedChannels = new Dictionary<int, Channel>();
            for (int i = 0; i < data.Length / _dataStructManager.FrameSize; i++)
            {
                int id = data[i * _dataStructManager.FrameSize];
                if (!parsedChannels.ContainsKey(id))
                {
                    parsedChannels[id] = new Channel(_dataStructManager.GateAmpsOffset.Count);
                }

                var singleParsed = SinglePointConverter(data, i * _dataStructManager.FrameSize, _dataStructManager);
                for (int j = 0; j < singleParsed.Count; j++)
                {
                    parsedChannels[id].Gates[j].GatePoints.Add(singleParsed[j]);
                }
            }
            return parsedChannels.Values.ToList();
        }
        /// <summary>
        /// Парс одной координаты по указаному офсету
        /// </summary>
        /// <param name="rawArray">Сырые данные, структура как в DataType</param>
        /// <param name="coordinateOffset">Офсет конкретной координаты</param>
        /// <param name="manager">DataType менеджер</param>
        /// <returns>Список координат по гейтам</returns>
        private List<PointPair> SinglePointConverter(byte[] rawArray, int coordinateOffset, DataTypeManager manager)
        {
            List<PointPair> res = new List<PointPair>(manager.GateAmpsOffset.Count);
            int id = rawArray[coordinateOffset];
            double x = BitConverter.ToUInt32(rawArray, manager.PointXOffset + coordinateOffset) * _multiplier;// - _channelsStartOffset[id];
            foreach (var offset in manager.GateAmpsOffset)
            {
                res.Add(new PointPair(x, rawArray[offset + coordinateOffset]));
            }
            return res;
        }


        #endregion
    }

    ///// <summary>
    ///// Клас для сохранения точек отрисовки
    ///// </summary>
    //internal class Channel
    //{
    //    private List<Gate> _gates;

    //    public int ChannelId { get; set; }
    //    public List<Gate> Gates
    //    {
    //        get
    //        {
    //            return _gates;
    //        }
    //    }

    //    public Channel(int gateCount)
    //    {
    //        _gates = new List<Gate>(gateCount);
    //        for (int i = 0; i < gateCount; i++)
    //        {
    //            _gates.Add(new Gate());
    //            _gates[i].Ascans = new List<PointPairList>();
    //            _gates[i].GatePoints = new List<PointPair>();
    //        }
    //    }

    //}

    ///// <summary>
    ///// 
    ///// </summary>
    //internal class Gate
    //{
    //    public List<PointPair> GatePoints { get; set; }
    //    public List<PointPairList> Ascans { get; set; }
    //}
}
