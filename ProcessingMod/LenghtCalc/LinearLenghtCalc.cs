using NCE.CommonData;
using NCE.ModulesCommonData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace NCE.UTscanner.Processing.LenghtCalc
{
    public class LinearLenghtCalc : IRawDataInputModule, IModule
    {
        private Dictionary<int, double> _lenght;
        private double _step;
        private DataTypeManager _manager;
        private ActionBlock<byte[]> _actBlock;
        private Dictionary<int, double> _channelsStartOffset;
        private double _channelsDeadZoneStartOffset;

        public Dictionary<int, double> ChannelDataLenght
        {
            get
            {
                return _lenght;
            }
        }

        public LinearLenghtCalc(DataTypeManager manager, double step, Dictionary<int, double> channelsStartOffset, double channelsDeadZoneStartOffset)
        {
            _manager = manager;
            _step = step;
            _channelsStartOffset = channelsStartOffset;
            _lenght = new Dictionary<int, double>(channelsStartOffset.Count);
            _channelsDeadZoneStartOffset = channelsDeadZoneStartOffset;

            foreach (var kvp in channelsStartOffset)
            {
                _lenght.Add(kvp.Key, 0);
            }

            _actBlock = new ActionBlock<byte[]>(new Action<byte[]>(CalcLenght));
        }

        public Task Completion
        {
            get
            {
                return _actBlock.Completion;
            }
        }
        public string ModuleName
        {
            get
            {
                return "LenghtCalc";
            }
        }
        public void Complete()
        {
            _actBlock.Complete();
        }

        public void Fault(Exception exception)
        {
            throw new NotImplementedException();
        }

        public void PostData(byte[] raw)
        {
            _actBlock.Post(raw);
        }

        private void CalcLenght(byte[] raw)
        {
            for (int i = 0; i < raw.Length / _manager.FrameSize; i+=_manager.FrameSize)
            {
                int id = raw[i];
                double xCoord = BitConverter.ToUInt32(raw, i + _manager.PointXOffset) * _step - _channelsStartOffset[id];
                if (xCoord > _channelsDeadZoneStartOffset)
                    _lenght[id] = xCoord;
            }
        }        
    }
}
