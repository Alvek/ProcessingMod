using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using NCE.CommonData;
using NCE.ModulesCommonData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace NCE.Processing.Converters
{
    /// <summary>
    /// Применяет БПФ к аскану и отдает дальше по цепочке измененный массив
    /// </summary>
    public class FFTConverter : IPropagatorBlock<byte[], ConvertedFFT>, IRawDataInputModule
    {
        /// <summary>
     /// Блок конвертации из сырых данных в точки отрисовки
     /// </summary>
        private readonly TransformBlock<byte[], ConvertedFFT> _converterBlock;
        /// <summary>
        /// DataType менеджер
        /// </summary>
        private readonly DataTypeManager _dataStructManager;
        /// <summary>
        /// Функция конвертации
        /// </summary>
        private Func<byte[], ConvertedFFT> convert;
        private MathNet.Numerics.Complex32[] _complArray;
        private bool _removeAverageValue = false;

        public Task Completion
        {
            get { return _converterBlock.Completion; }
        }

        public string ModuleName
        { get { return "FFTConverter"; } }

        public void Complete()
        {
            _converterBlock.Complete();
        }

        public FFTConverter(DataTypeManager dataStructManager, bool removeAverageValue)
        {
            _removeAverageValue = removeAverageValue;
            _dataStructManager = dataStructManager;
            convert = AscanFFT;
            int size = DataTypeManager.AscanMaxPointCount;
            if (IsPowerOfTwo((ulong)size))
            {
                _complArray = new MathNet.Numerics.Complex32[size];
            }
            else
            {
                int newSize = 2;
                while (newSize < size)
                    newSize *= 2;

                _complArray = new MathNet.Numerics.Complex32[newSize];
            }            

            _converterBlock = new TransformBlock<byte[], ConvertedFFT>(convert, new ExecutionDataflowBlockOptions() { SingleProducerConstrained = true, MaxDegreeOfParallelism = 1 });
        }

        #region DataFlow
        public ConvertedFFT ConsumeMessage(DataflowMessageHeader messageHeader, ITargetBlock<ConvertedFFT> target, out bool messageConsumed)
        {
            return ((IPropagatorBlock<byte[], ConvertedFFT>)_converterBlock).ConsumeMessage(messageHeader, target, out messageConsumed);
        }

        public void Fault(Exception exception)
        {
            ((IPropagatorBlock<byte[], ConvertedFFT>)_converterBlock).Fault(exception);
        }

        public IDisposable LinkTo(ITargetBlock<ConvertedFFT> target, DataflowLinkOptions linkOptions)
        {
            return _converterBlock.LinkTo(target, linkOptions);
        }

        public DataflowMessageStatus OfferMessage(DataflowMessageHeader messageHeader, byte[] messageValue, ISourceBlock<byte[]> source, bool consumeToAccept)
        {
            return ((IPropagatorBlock<byte[], ConvertedFFT>)_converterBlock).OfferMessage(messageHeader, messageValue, source, consumeToAccept);
        }

        public void ReleaseReservation(DataflowMessageHeader messageHeader, ITargetBlock<ConvertedFFT> target)
        {
            ((IPropagatorBlock<byte[], ConvertedFFT>)_converterBlock).ReleaseReservation(messageHeader, target);
        }

        public bool ReserveMessage(DataflowMessageHeader messageHeader, ITargetBlock<ConvertedFFT> target)
        {
            return ((IPropagatorBlock<byte[], ConvertedFFT>)_converterBlock).ReserveMessage(messageHeader, target);
        }
        #endregion

        public void PostData(byte[] raw)
        {
            _converterBlock.Post(raw);
        }

        private ConvertedFFT AscanFFT(byte[] rawData)
        {
            ConvertedFFT res = new ConvertedFFT(rawData);
            int size = _dataStructManager.FrameSize;

            for (int frame = 0; frame < rawData.Length / size; frame += size)
            {
                SingleFFTConvert(rawData, frame);
                Complex32[] halfArray = new Complex32[_complArray.Length / 2];
                Array.Copy(_complArray, halfArray, _complArray.Length / 2);
                Spectr spec = new Spectr(halfArray, rawData[frame]);

                res.Spectr.Add(spec);
            }

            return res;
        }

        private void SingleFFTConvert(byte[] rawData, int curFrameStartOffset)
        {
            int startOffset = curFrameStartOffset + _dataStructManager.AscanPointOffset;
            float avg = 0;

            float[] amps = new float[_complArray.Length];

            for (int i = startOffset, j = 0;
               i < startOffset + _complArray.Length;
               i++, j++)
            {
                if (rawData[i] != 0)
                {
                    amps[j] = rawData[i];
                    avg += rawData[i];
                }
                else
                {
                    amps[j] = 128;
                    avg += 128;
                }
            }

            if (_removeAverageValue)
            {
                avg /= 512;

                for (int i = 0; i < _complArray.Length; i++)
                {
                    amps[i] -= avg;
                }
            }


            for (int i = 0; i < _complArray.Length; i++)
            {
                _complArray[i] = new Complex32(amps[i], 0);
            }

            Fourier.Forward(_complArray);
        }
        
        private bool IsPowerOfTwo(ulong x)
        {
            return (x & (x - 1)) == 0;
        }
    }
}
