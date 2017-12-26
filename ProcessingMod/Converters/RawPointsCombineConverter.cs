using NCE.CommonData;
using NCE.ModulesCommonData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace NCE.Processing.Drawnig
{
    /// <summary>
    /// Суммирует сырые данные разных каналов в один новый
    /// </summary>
    public class RawPointsCombineConverter : ITargetBlock<byte[]>, ISourceBlock<List<Channel>>, IRawDataInputModule
    {
        private const double _maxAmp = 100;

        private BufferBlock<byte[]> _inputBuffer = new BufferBlock<byte[]>();
        private ActionBlock<byte[]> _combineAct;
        private BufferBlock<List<Channel>> _outputBuffer = new BufferBlock<List<Channel>>();
        private List<Dictionary<double, SummTemp>> _dataToCombine;// = new List<Dictionary<int, SummTemp>>();
        private List<CombineRule> _rules;
        private DataTypeManager _manager;
        private List<Dictionary<double, SummTemp>> _tempStorage;// = new List<Dictionary<double, SummTemp>>();
        private double _step;
        private double[] _channelsStartXPoint;
        private double[] _currentChannelXPoint;

        public RawPointsCombineConverter(CombineSettings combSett, DataTypeManager manager, double multiplier)
        {
            if (combSett.Rules.Count < 1)
                throw new ArgumentException("No combine rules set!");

            _manager = manager;
            _step = multiplier;

            _rules = combSett.Rules;

            _channelsStartXPoint = new double[_rules.Count];
            _currentChannelXPoint = new double[_rules.Count];
            for (int rule = 0; rule < _rules.Count; rule++)
            {
                _channelsStartXPoint[rule] = int.MinValue;
            }

            _dataToCombine = new List<Dictionary<double, SummTemp>>(combSett.Rules.Count);
            _tempStorage = new List<Dictionary<double, SummTemp>>(combSett.Rules.Count);
            for (int i = 0; i < combSett.Rules.Count; i++)
            {
                _dataToCombine.Add(new Dictionary<double, SummTemp>());
                _tempStorage.Add(new Dictionary<double, SummTemp>());
            }


            _combineAct = new ActionBlock<byte[]>(
                new Action<byte[]>(Combine),
                new ExecutionDataflowBlockOptions() { SingleProducerConstrained = true, MaxDegreeOfParallelism = 1 }
                );
            _combineAct.Completion.ContinueWith(
                (t) =>
                {
                    _outputBuffer.Complete();
                }
                );

            SummTemp.MaxAmpValue = _maxAmp;

            //PropagateCompletion - обязателен, авто передача завершения работы модулю подсчета.
            _inputBuffer.LinkTo(_combineAct, new DataflowLinkOptions() { PropagateCompletion = true });

        }

        public string ModuleName
        {
            get
            {
                return "RawPointsCombineConverter";
            }
        }

        #region Dataflow block
        public Task Completion
        {
            get
            {
                return _outputBuffer.Completion;
            }
        }

        public void Complete()
        {
            _inputBuffer.Complete();
        }

        public List<Channel> ConsumeMessage(DataflowMessageHeader messageHeader, ITargetBlock<List<Channel>> target, out bool messageConsumed)
        {
            return ((ISourceBlock<List<Channel>>)_outputBuffer).ConsumeMessage(messageHeader, target, out messageConsumed);
        }

        public void Fault(Exception exception)
        {
            ((ISourceBlock<List<Channel>>)_outputBuffer).Fault(exception);
        }

        public IDisposable LinkTo(ITargetBlock<List<Channel>> target, DataflowLinkOptions linkOptions)
        {
            return _outputBuffer.LinkTo(target, linkOptions);
        }

        public DataflowMessageStatus OfferMessage(DataflowMessageHeader messageHeader, byte[] messageValue, ISourceBlock<byte[]> source, bool consumeToAccept)
        {
            return ((ITargetBlock<byte[]>)_inputBuffer).OfferMessage(messageHeader, messageValue, source, consumeToAccept);
        }

        public void PostData(byte[] raw)
        {
            _inputBuffer.Post(raw);
        }

        public void ReleaseReservation(DataflowMessageHeader messageHeader, ITargetBlock<List<Channel>> target)
        {
            ((ISourceBlock<List<Channel>>)_outputBuffer).ReleaseReservation(messageHeader, target);
        }

        public bool ReserveMessage(DataflowMessageHeader messageHeader, ITargetBlock<List<Channel>> target)
        {
            return ((ISourceBlock<List<Channel>>)_outputBuffer).ReserveMessage(messageHeader, target);
        }
        #endregion

        private void Combine(byte[] raw)
        {
            foreach (var dict in _tempStorage)
                dict.Clear();

            List<Channel> readyList = new List<Channel>();
            for (int i = 0; i < raw.Length / _manager.FrameSize; i++)
            {
                int channelId = _manager.GetChannel(raw[_manager.FrameSize * i]);
                for (int rule = 0; rule < _rules.Count; rule++)
                {
                    if (_rules[rule].ChannelsIds.Contains(channelId))
                    {
                        double x = BitConverter.ToUInt32(raw, _manager.PointXOffset + _manager.FrameSize * i) * _step;

                        if (_channelsStartXPoint[rule] == int.MinValue)
                        {
                            _channelsStartXPoint[rule] = x;
                            _currentChannelXPoint[rule] = x - _step;
                        }

                        for (int gate = 0; gate < _manager.GateAmpsOffset.Count; gate++)
                        {
                            //Проверяем нужно ли сумировать этот гейт
                            if ((_rules[rule].GateCombineMask & gate * gate) == gate)
                            {
                                //uint amp = BitConverter.ToUInt32(raw, _manager.PointYOffset + _manager.FrameSize * i);
                                double amp = _manager.GetAmp(raw, _manager.PointYOffset + _manager.FrameSize * i, gate);
                                if (!_tempStorage[rule].ContainsKey(x))
                                {
                                    if (!_dataToCombine[rule].ContainsKey(x))
                                        _tempStorage[rule][x] = new SummTemp(x, amp, gate, channelId, _rules[rule].ChannelsIds, _rules[rule].GatesCount, 
                                            _rules[rule].DisplayId);
                                    else
                                        _tempStorage[rule][x] = _dataToCombine[rule][x];
                                }
                                else
                                {
                                    _tempStorage[rule][x].AddAmp(gate, amp, channelId);
                                }
                            }
                        }
                        ////Проверить что мы Ready
                        //if (_dataToCombine[rule][x].IsReady())
                        //{
                        //    if (!_tempStorage.ContainsKey(channelId))
                        //    {
                        //        Channel readyData = new Channel(_manager.GateAmpsOffset.Count);
                        //        readyData.ChannelId = channelId;
                        //        for (int gate = 0; gate < readyData.Gates.Count; gate++)
                        //        {
                        //            readyData.Gates[gate].GatePoints = new List<ZedGraph.PointPair>();
                        //            readyData.Gates[gate].GatePoints.Add(new ZedGraph.PointPair(_dataToCombine[rule][x].XCoord[gate], 
                        //                _dataToCombine[rule][x].YCoord[gate]));
                        //        }
                        //        _dataToCombine[rule].Remove(x);
                        //    }
                        //    else
                        //    {
                        //        for (int gate = 0; gate < _manager.GateAmpsOffset.Count; gate++)
                        //        {
                        //            _tempStorage[channelId].Gates[gate].GatePoints.Add(new ZedGraph.PointPair(_dataToCombine[rule][channelId].XCoord[gate],
                        //                _dataToCombine[rule][channelId].YCoord[gate]));
                        //        }
                        //        _dataToCombine[rule].Remove(x);
                        //    }
                        //}
                    }
                }
            }
            List<Channel> ch = new List<Channel>();
            int ruleIdx = 0;
            ch = new List<Channel>(_rules.Count);
            foreach (var ruleDict in _tempStorage)
            {
                ch.Add(new Channel(_rules[ruleIdx].GatesCount));
                foreach (var pointKvp in ruleDict)
                {
                    SummTemp.CachedResult oldRes = pointKvp.Value.CachedReady;
                    bool curRes = pointKvp.Value.IsReady();
                    ch[ruleIdx].ChannelId = pointKvp.Value.DisplayChannelId;
                    if (curRes)
                    {
                        if (_currentChannelXPoint[ruleIdx] + _step == pointKvp.Value.XCoord[0])
                        {
                            for (int gate = 0; gate < _rules[ruleIdx].GatesCount; gate++)
                            {
                                ch[ruleIdx].Gates[gate].GatePoints.Add(new ZedGraph.PointPair(pointKvp.Value.XCoord[gate], pointKvp.Value.YCoord[gate]));
                            }
                            if (oldRes == SummTemp.CachedResult.NotReady)//добавлен ранее, но не хватало данных по каналам\гейтам и остался ждать недостающих данных
                            {
                                _dataToCombine[ruleIdx].Remove(pointKvp.Key);
                            }
                            _currentChannelXPoint[ruleIdx] += _step;
                        }
                        else //не хватает данных, оставляем ждать недостачу
                        {
                            _dataToCombine[ruleIdx].Add(pointKvp.Key, pointKvp.Value);
                        }
                    }
                    else //не хватает данных, оставляем ждать недостачу
                    {
                        _dataToCombine[ruleIdx].Add(pointKvp.Key, pointKvp.Value);
                    }
                }
                ruleIdx++;
            }
            ruleIdx = 0;
            foreach (var ruleDict in _dataToCombine)
            {
                //if(ch == null)
                //    ch = new List<Channel>(_rules[ruleIdx].GatesCount);

                foreach (var pointKvp in ruleDict)
                {
                    bool curRes = pointKvp.Value.IsReady();
                    if (curRes)
                    {
                        if (_currentChannelXPoint[ruleIdx] + _step == pointKvp.Value.XCoord[0])
                        {
                            for (int gate = 0; gate < _rules[ruleIdx].GatesCount; gate++)
                            {
                                ch[ruleIdx].Gates[gate].GatePoints.Add(new ZedGraph.PointPair(pointKvp.Value.XCoord[gate], pointKvp.Value.YCoord[gate]));
                            }
                            _dataToCombine[ruleIdx].Remove(pointKvp.Key);                            
                            _currentChannelXPoint[ruleIdx] += _step;
                        }
                    }
                }
                ruleIdx++;
            }
            _outputBuffer.Post(ch);
        }
    }

    class SummTemp
    {
        public static double MaxAmpValue { get; set; }

        public enum CachedResult
        {
            NotCached = 0,
            Ready,
            NotReady,
        }

        // Rоординаты в сокомате для отного каналана\гейта не повторяются и можно не сверять какие каналы уже сохранены
        private int _displayId;

        /// <summary>
        /// X координата
        /// </summary>
        private double[] _xCoord;
        ///// <summary>
        ///// Y координата
        ///// </summary>
        private double[] _yCoord;
        private int _gatesCount;
        private List<int>[] _ruleChannelsIds;
        private List<int>[] _channelsIdsToCollect;

        private CachedResult _cachedReady;

        public double[] XCoord { get { return _xCoord; } }
        public double[] YCoord { get { return _yCoord; } }
        public int GatesCount { get { return _gatesCount; } }
        public CachedResult CachedReady { get { return _cachedReady; } }
        public int DisplayChannelId { get { return _displayId; } }

        public SummTemp(double xCoord, double amp, int gate, int curChannelId, List<int> ruleChannelIds, int gatesCount, int displayChannelId)
        {
            _xCoord = new double[gatesCount];
            _yCoord = new double[gatesCount];

            _xCoord[gate] = xCoord;
            _yCoord[gate] = amp;

            if (_yCoord[gate] > MaxAmpValue)
                _yCoord[gate] = MaxAmpValue;

            _ruleChannelsIds = new List<int>[gatesCount];
            _channelsIdsToCollect = new List<int>[gatesCount];
            for (int i = 0; i < gatesCount; i++)
            {
                _ruleChannelsIds[i] = new List<int>(ruleChannelIds);
                _channelsIdsToCollect[i] = new List<int>(ruleChannelIds);
            }
            _channelsIdsToCollect[gate].Remove(curChannelId);
            _displayId = displayChannelId;
            _gatesCount = gatesCount;
        }

        public void AddAmp(int gate, double amp, int channelId)
        {
            if (_channelsIdsToCollect[gate].Contains(channelId))
            {
                _yCoord[gate] += amp;
                if (_yCoord[gate] > MaxAmpValue)
                    _yCoord[gate] = MaxAmpValue;
                _channelsIdsToCollect[gate].Remove(channelId);
            }
            else
            {
                throw new ArgumentException(string.Format("Gate:{0}, ChannelId:{1} is already added!", gate, channelId));
            }
        }

        public bool IsReady()
        {
            bool res;
            if (_cachedReady == CachedResult.Ready)
            {
                res = true;
            }
            else
            {
                res = true;
                for (int gate = 0; gate < _channelsIdsToCollect.Length; gate++)
                {
                    if (_channelsIdsToCollect[gate].Count != 0)
                    {
                        res = false;
                        break;
                    }
                }
                if (res)
                    _cachedReady = CachedResult.Ready;
                else
                    _cachedReady = CachedResult.NotReady;
            }
            return res;
        }

    }
}
