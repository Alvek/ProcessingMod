using NCE.CommonData;
using NCE.ModulesCommonData;
using NCE.Processing.Drawing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using ZedGraph;

namespace NCE.UTscanner.Processing.Drawing
{
    public class AscanDrawer : IConvertedSplitterTarget, IReceivableSourceBlock<List<Channel>>, ITargetBlock<List<Channel>>, IModule
    {

        /// <summary>
        /// Настройки отрисовки
        /// </summary>
        private DrawSettings[] _drawSettings;
        /// <summary>
        /// ZedGraph контролы
        /// </summary>
        private ZedGraphControl[] _zedControls;
        /// <summary>
        /// Внутренний буфер
        /// </summary>
        private BufferBlock<List<Channel>> _innerBuffer = new BufferBlock<List<Channel>>();
        /// <summary>
        /// Блок отрисовки
        /// </summary>
        private ActionBlock<List<Channel>> _drawerBlock;
        /// <summary>
        /// Менеджер
        /// </summary>        
        private DataTypeManager _dataStructManager;
        /// <summary>
        /// Список точек для отрисовки [ZedControl][Id][Gate]
        /// </summary>
        private ClassicAscanPoints[][] _channelPoints;
        /// <summary>
        /// Список для связывания Id с массивом точек
        /// </summary>
        private Dictionary<int, ClassicAscanPoints> _channelToPointArr;

        public Task Completion
        {
            get { return _drawerBlock.Completion; }
        }
        public string ModuleName
        {
            get { return "AscanDrawer"; }
        }
        public void Complete()
        {
            _innerBuffer.Complete();
        }

        public AscanDrawer(ZedGraphControl[] zedControls, DrawSettings[] drawSettings, DataTypeManager dataStructManager)
        {
            if (zedControls == null)
                throw new ArgumentNullException("ZedControls array can't be null!");
            if (zedControls.Length == 0)
                throw new ArgumentException("ZedControls array have zero lenght!");
            if (drawSettings == null)
                throw new ArgumentNullException("DrawSettings array can't be null!");
            if (drawSettings.Length == 0)
                throw new ArgumentException("DrawSettings array have zero lenght!");

            if (zedControls.Length != drawSettings.Length)
                throw new ArgumentException("ZedGraph controls and drawSettings count different!");
            
            _zedControls = zedControls;
            _drawSettings = drawSettings;
            _dataStructManager = dataStructManager;


            _channelPoints = new ClassicAscanPoints[zedControls.Length][];
            _channelToPointArr = new Dictionary<int, ClassicAscanPoints>(zedControls.Length * 2);
            for (int zedControl = 0; zedControl < drawSettings.Length; zedControl++)
            {
                _channelPoints[zedControl] = new ClassicAscanPoints[drawSettings[zedControl].ChannelSettings.Length];
                for (int channelIdx = 0; channelIdx < _channelPoints[zedControl].Length; channelIdx++)
                {
                    _channelPoints[zedControl][channelIdx] = new ClassicAscanPoints();                    
                    _channelToPointArr[drawSettings[zedControl].ChannelSettings[channelIdx].ChannelIdx] = _channelPoints[zedControl][channelIdx];
                }
            }

            Action<List<Channel>> act = Draw;
            _drawerBlock = new ActionBlock<List<Channel>>(act, new ExecutionDataflowBlockOptions() { MaxDegreeOfParallelism = 1, });//com maxDeg
            //Отрисовка последних точек по завершению сбора
            _drawerBlock.Completion.ContinueWith(
                t => { InvalidatePointOnComplete(); }
            );

            InitZedControls(drawSettings, zedControls);//, deadZoneColor);
            //PropagateCompletion - обазятелен, передача завершения сбора в блок отрисовки
            _innerBuffer.LinkTo(_drawerBlock, new DataflowLinkOptions() { PropagateCompletion = true });
        }

        #region DataFlow
        public List<Channel> ConsumeMessage(DataflowMessageHeader messageHeader, ITargetBlock<List<Channel>> target, out bool messageConsumed)
        {
            return ((IReceivableSourceBlock<List<Channel>>)_innerBuffer).ConsumeMessage(messageHeader, target, out messageConsumed);
        }

        public void Fault(Exception exception)
        {
            throw new NotImplementedException();
        }

        public IDisposable LinkTo(ITargetBlock<List<Channel>> target, DataflowLinkOptions linkOptions)
        {
            return _innerBuffer.LinkTo(target, linkOptions);
        }

        public DataflowMessageStatus OfferMessage(DataflowMessageHeader messageHeader, List<Channel> messageValue, ISourceBlock<List<Channel>> source, bool consumeToAccept)
        {
            return ((ITargetBlock<List<Channel>>)_innerBuffer).OfferMessage(messageHeader, messageValue, source, consumeToAccept);
        }
        
        public void ReleaseReservation(DataflowMessageHeader messageHeader, ITargetBlock<List<Channel>> target)
        {
            ((IReceivableSourceBlock<List<Channel>>)_innerBuffer).ReleaseReservation(messageHeader, target);
        }

        public bool ReserveMessage(DataflowMessageHeader messageHeader, ITargetBlock<List<Channel>> target)
        {
            return ((IReceivableSourceBlock<List<Channel>>)_innerBuffer).ReserveMessage(messageHeader, target);
        }

        public bool TryReceive(Predicate<List<Channel>> filter, out List<Channel> item)
        {
            return _innerBuffer.TryReceive(filter, out item);
        }

        public bool TryReceiveAll(out IList<List<Channel>> items)
        {
            return _innerBuffer.TryReceiveAll(out items);
        }
        #endregion

        public void PostData(List<Channel> channels)
        {
            _innerBuffer.Post(channels);
        }

        private void Draw(List<Channel> convertedData)
        {
            foreach (var ch in convertedData)
            {
                //_channelToPointArr[ch.ChannelId] = new ClassicAscanPoints();
                _channelToPointArr[ch.ChannelId].UpdatePoint(ch.Gates[0].GatePoints.ToArray());


            }
        }

        /// <summary>
        /// Инициализация настроек
        /// </summary>
        /// <param name="settings">Настройки отрисовки</param>
        /// <param name="zedControls">Контролы</param>
        private void InitZedControls(DrawSettings[] settings, ZedGraphControl[] zedControls)//, Color deadZoneColor)
        {
            for (int i = 0; i < zedControls.Length; i++)
            {
                GraphPane pane = zedControls[i].GraphPane;
                pane.CurveList.Clear();

                for (int channel = 0; channel < settings[i].ChannelSettings.Length; channel++)
                {
                    LineItem myCurve;
                    myCurve = pane.AddCurve(pane.Title.Text, _channelPoints[i][channel], settings[i].ChannelSettings[0].GateColors[0]);
                    // Тип заполнения - сплошная заливка
                    myCurve.Symbol.Fill.Type = FillType.Solid;
                    // Размер ромбиков
                    myCurve.Symbol.Size = 1;
                }
                pane.AxisChange();
            }
        }

        /// <summary>
        /// Перерисовка по окончанию сбора
        /// </summary>
        private void InvalidatePointOnComplete()
        {
            foreach (var zed in _zedControls)
            {
                zed.Invalidate();
            }
        }
    }
}
