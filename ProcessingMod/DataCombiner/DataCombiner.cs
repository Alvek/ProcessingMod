using NCE.ModulesCommonData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace NCE.UTscanner.Processing.DataCombiner
{
    public class DataCombiner : IPropagatorBlock<byte[], byte[]>, IRawDataInputModule
    {
        /// Блок конвертации из сырых данных в точки отрисовки
        /// </summary>
        private readonly TransformBlock<byte[], byte[]> _converterBlock;
        private Func<byte[], byte[]> _convert;
        private byte[] _temp;
        private int _frameSize = 0;

        public DataCombiner(int frameSize)
        {
            _frameSize = frameSize;
            _convert = Convert;
            _converterBlock = new TransformBlock<byte[], byte[]>(_convert, new ExecutionDataflowBlockOptions() { SingleProducerConstrained = true, MaxDegreeOfParallelism = 1 });
        }

        public Task Completion => throw new NotImplementedException();

        public string ModuleName
        {
            get { return "DataCombiner"; }
        }

        public void Complete()
        {
            _converterBlock.Complete();
        }

        #region DataFlow

        public byte[] ConsumeMessage(DataflowMessageHeader messageHeader, ITargetBlock<byte[]> target, out bool messageConsumed)
        {
            return ((IPropagatorBlock<byte[], byte[]>)_converterBlock).ConsumeMessage(messageHeader, target, out messageConsumed);
        }

        public void Fault(Exception exception)
        {
            ((IPropagatorBlock<byte[], byte[]>)_converterBlock).Fault(exception);
        }

        public IDisposable LinkTo(ITargetBlock<byte[]> target, DataflowLinkOptions linkOptions)
        {
            return _converterBlock.LinkTo(target, linkOptions);
        }

        public DataflowMessageStatus OfferMessage(DataflowMessageHeader messageHeader, byte[] messageValue, ISourceBlock<byte[]> source, bool consumeToAccept)
        {
            return ((IPropagatorBlock<byte[], byte[]>)_converterBlock).OfferMessage(messageHeader, messageValue, source, consumeToAccept);
        }

        public void ReleaseReservation(DataflowMessageHeader messageHeader, ITargetBlock<byte[]> target)
        {
            ((IPropagatorBlock<byte[], byte[]>)_converterBlock).ReleaseReservation(messageHeader, target);
        }

        public bool ReserveMessage(DataflowMessageHeader messageHeader, ITargetBlock<byte[]> target)
        {
            return ((IPropagatorBlock<byte[], byte[]>)_converterBlock).ReserveMessage(messageHeader, target);
        }
        #endregion

        public void PostData(byte[] raw)
        {
            _converterBlock.Post(raw);
        }

        private byte[] Convert(byte[] data)
        {
            if (data.Length % _frameSize != 0 && _temp == null)
            {
                int bytesToCopy = data.Length - (data.Length % _frameSize);
                byte[] completeFramesData = new byte[bytesToCopy];
                Buffer.BlockCopy(data, 0, completeFramesData, 0, bytesToCopy);
                _temp = new byte[data.Length % _frameSize];
                Buffer.BlockCopy(data, bytesToCopy, _temp, 0, data.Length % _frameSize);
                return completeFramesData;
            }
            else if (data.Length % _frameSize != 0 && _temp != null)
            {
                byte[] completeFramesData = new byte[(data.Length + _temp.Length) - ((data.Length + _temp.Length) % _frameSize)];
                Buffer.BlockCopy(_temp, 0, completeFramesData, 0, _temp.Length);
                if ((data.Length + _temp.Length) % _frameSize == 0)
                {
                    Buffer.BlockCopy(data, 0, completeFramesData, _temp.Length, data.Length);
                    _temp = null;
                    return completeFramesData;
                }
                else
                {
                    int bytesToCopy = (data.Length + _temp.Length) - ((data.Length + _temp.Length) % _frameSize);
                    Buffer.BlockCopy(data, 0, completeFramesData, _temp.Length, bytesToCopy);
                    _temp = new byte[data.Length - bytesToCopy];
                    Buffer.BlockCopy(data, bytesToCopy, _temp, 0, data.Length - bytesToCopy);
                    return completeFramesData;
                }
            }
            else if (data.Length % _frameSize == 0 && _temp == null)
            {
                return data;
            }
            else if (data.Length % _frameSize == 0 && _temp != null)
            {
                byte[] completeFramesData = new byte[(data.Length + _temp.Length) - ((data.Length + _temp.Length) % _frameSize)];
                Buffer.BlockCopy(_temp, 0, completeFramesData, 0, _temp.Length);
                if ((data.Length + _temp.Length) % _frameSize == 0)
                {
                    Buffer.BlockCopy(data, 0, completeFramesData, _temp.Length, data.Length);
                    _temp = null;
                    return completeFramesData;
                }
                else
                {
                    int bytesToCopy = (data.Length + _temp.Length) - ((data.Length + _temp.Length) % _frameSize);
                    Buffer.BlockCopy(data, 0, completeFramesData, _temp.Length, bytesToCopy);
                    _temp = new byte[data.Length - bytesToCopy];
                    Buffer.BlockCopy(data, bytesToCopy, _temp, 0, data.Length - bytesToCopy);
                    return completeFramesData;
                }
            }
            else
                throw new Exception("Something bad hapen!");

        }

    }
}
