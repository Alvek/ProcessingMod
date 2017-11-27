using NCE.CommonData;
using NCE.ModulesCommonData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace NCE.UTscanner.Processing.Drawnig
{
    public class RawPointsCombineConverter : ITargetBlock<byte[]>, ISourceBlock<List<Channel>>, IRawDataInputModule
    {

        private BufferBlock<byte[]> _inputBuffer;// = new BufferBlock<byte[]>();
        private ActionBlock<byte[]> _combineAct;
        private BufferBlock<List<Channel>> _outputBuffer = new BufferBlock<List<Channel>>();
        private List<Dictionary<int, SummTemp>> _dataToCombine;// = new List<Dictionary<int, SummTemp>>();
        private List<CombineRule> _rules;
        private DataTypeManager _manager;
        private double _multiplier;
        public RawPointsCombineConverter(CombineSettings combSett, DataTypeManager manager, double multiplier)
        {
            if (combSett.Rules.Count < 1)
                throw new ArgumentException("No combine rules set!");

            _manager = manager;
            _multiplier = multiplier;

            _rules = combSett.Rules;
            _dataToCombine = new List<Dictionary<int, SummTemp>>(combSett.Rules.Count);
            for(int i = 0; i < _dataToCombine.Count; i++)
            {
                _dataToCombine[i] = new Dictionary<int, SummTemp>();
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
            for (int i = 0; i < raw.Length / _manager.FrameSize; i++)
            {
                int channelId = _manager.GetChannel(raw[_manager.FrameSize * i]);
                for(int rule = 0; i < _rules.Count; rule++)
                {
                    if (_rules[rule].ChannelsIds.Contains(channelId))
                    {
                        for(int gate = 0; gate < _manager.GateAmpsOffset.Count; gate++)
                        {
                            //Проверяем нужно ли сумировать этот гейт
                            if ((_rules[rule].GateCombineMask & gate) == gate)
                            {
                                uint amp = BitConverter.ToUInt32(raw, _manager.PointYOffset + _manager.FrameSize * i);
                                if (!_dataToCombine[rule].ContainsKey(channelId))
                                {
                                    double x = BitConverter.ToUInt32(raw, _manager.PointXOffset + _manager.FrameSize * i) * _multiplier;
                                    _dataToCombine[rule][channelId] = new SummTemp(x, _rules[rule].ChannelsIds.Count, _rules[rule].Gates);
                                }
                                else
                                {
                                    _dataToCombine[rule][channelId].AddAmp(gate, amp);
                                }
                            }
                        }
                        //Проверить что мы Ready
                        //SummTemp.REady
                    }
                }
            }
        }
    }

    class SummTemp
    {
        // Rоординаты в сокомате для отного каналана\гейта не повторяются и можно не сверять какие каналы уже сохранены
        
        /// <summary>
        /// X координата
        /// </summary>
        private double[] _xCoord;
        /// <summary>
        /// Y координата
        /// </summary>
        private uint[] _yCoord;
        /// <summary>
        /// Колиство просумированых каналов
        /// </summary>
        private int[] _currentSummCount;
        /// <summary>
        /// Счетчик сумирований, по достижению заданого количества отдаем на отрисовку
        /// </summary>
        private int[] _targetSummCount;
        private int[] _gates;
        //private List<int> collectedChannels;
        public double[] XCoord { get => _xCoord; }
        public int[] CurrentSummCount { get => _currentSummCount; }
        public int[] TargetSummCount { get => _targetSummCount; }
        public uint[] YCoord { get => _yCoord; }
        public int[] Gates { get => _gates;  }

        //public List<int> CollectedChannels { get => collectedChannels; set => collectedChannels = value; }


        public SummTemp(double xCoord, int targetSummCount, int[] gates)
        {
            _xCoord = new double[gates.Length];
            _targetSummCount = new int[gates.Length];
            _gates = gates;
        }

        public void AddAmp(int gate, uint amp)
        {
            _yCoord[gate] += amp;
            _currentSummCount[gate]++;            
        }

        public bool Ready()
        {
            bool res = true;
            for (int gate = 0; gate < _gates.Length; gate++)
            {
                if (_currentSummCount[gate] != _targetSummCount[gate])
                {
                    res = false;
                    break;
                }
            }
            return res;
        }

    }
}
