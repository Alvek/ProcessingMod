using NCE.CommonData;
using NCE.ModulesCommonData;
using NCE.Processing.Drawing;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using ZedGraph;

namespace NCE.Processing.Drawing
{
    public class Drawer : IConvertedSplitterTarget, ILightBarrierSplitterTarget, IReceivableSourceBlock<List<Channel>>, ITargetBlock<List<Channel>>, IModule
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
        /// Список точек для отрисовки [ZedControl][Id][Gate]
        /// </summary>
        private LinearBscanPoints[][][] _channelPoints;
        /// <summary>
        /// Внутренний буфер
        /// </summary>
        private BufferBlock<List<Channel>> _innerBuffer = new BufferBlock<List<Channel>>();
        /// <summary>
        /// Блок отрисовки
        /// </summary>
        private ActionBlock<List<Channel>> _drawerBlock;
        /// <summary>
        /// Список для связывания Id с массивом точек
        /// </summary>
        private Dictionary<int, LinearBscanPoints[]> _channelToPointArr;
        /// <summary>
        /// Менеджер
        /// </summary>        
        private DataTypeManager _dataStructManager;
        /// <summary>
        /// Политика управления роста массива точек
        /// </summary>
        private PointOverflowPolicy _policy;
        /// <summary>
        /// Список офсетов
        /// </summary>
        private Dictionary<int, double> _channelsProbeOffset = new Dictionary<int, double>();
        /// <summary>
        /// Список офсетов
        /// </summary>
        private /*Dictionary<int, double>*/double _channelsDeadZoneStartOffset;
        /// <summary>
        /// Флаг сохранения лайт барьера
        /// </summary>
        private bool _saveStopCoord = false;
        /// <summary>
        /// Флаг для игнорирования повторных попыток сохранить лайт барьера
        /// </summary>
        private bool _stopCoordSaved = false;
        /// <summary>
        /// Координата лайтбарьера
        /// </summary>
        private double _barriedCoord;

        public string ModuleName
        {
            get
            {
                return "Drawer";
            }
        }

        public Task Completion
        {
            get
            {
                return _drawerBlock.Completion;
            }
        }
        /// <summary>
        /// Клая для отрисовки линейного контроля
        /// </summary>
        /// <param name="zedControls">Контроля в которых будем рисовать</param>
        /// <param name="drawSettings">Настройки отрисовки</param>
        /// <param name="dataStructManager">Менеджер</param>
        /// <param name="policy">Политика роста массива точек</param>
        /// <param name="channelsDeadZoneStartOffset">Список офсетов</param>
        public Drawer(ZedGraphControl[] zedControls, DrawSettings[] drawSettings, DataTypeManager dataStructManager, PointOverflowPolicy policy, /*Dictionary<int, double>*/ double channelsDeadZoneStartOffset)
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

            _policy = policy;
            _zedControls = zedControls;
            _drawSettings = drawSettings;
            _dataStructManager = dataStructManager;
            _channelsDeadZoneStartOffset = channelsDeadZoneStartOffset;

            Action<List<Channel>> act = Draw;
            _drawerBlock = new ActionBlock<List<Channel>>(act, new ExecutionDataflowBlockOptions() { MaxDegreeOfParallelism = 1, });//com maxDeg
            //Отрисовка последних точек по завершению сбора
            _drawerBlock.Completion.ContinueWith(t =>
            { InvalidatePointOnComplete(); }
                );

            _channelPoints = new LinearBscanPoints[zedControls.Length][][];
            _channelToPointArr = new Dictionary<int, LinearBscanPoints[]>(zedControls.Length * 2);
            for (int zedControl = 0; zedControl < drawSettings.Length; zedControl++)
            {
                _channelPoints[zedControl] = new LinearBscanPoints[drawSettings[zedControl].ChannelSettings.Length][];
                for (int channelIdx = 0; channelIdx < _channelPoints[zedControl].Length; channelIdx++)
                {
                    _channelsProbeOffset.Add(_drawSettings[zedControl].ChannelSettings[channelIdx].ChannelIdx, _drawSettings[zedControl].ChannelSettings[channelIdx].StartOffset);
                    _channelPoints[zedControl][channelIdx] = new LinearBscanPoints[dataStructManager.GateAmpsOffset.Count];
                    for (int gate = 0; gate < dataStructManager.GateAmpsOffset.Count; gate++)
                    {
                        _channelPoints[zedControl][channelIdx][gate] = new LinearBscanPoints(_drawSettings[zedControl].ChannelSettings[channelIdx].PointCount, _policy);
                    }
                    _channelToPointArr[drawSettings[zedControl].ChannelSettings[channelIdx].ChannelIdx] = _channelPoints[zedControl][channelIdx];
                }
            }



            InitZedControls(drawSettings, zedControls);
            //PropagateCompletion - обазятелен, передача завершения сбора в блок отрисовки
            _innerBuffer.LinkTo(_drawerBlock, new DataflowLinkOptions() { PropagateCompletion = true });
        }
        #region Public
        #region DataFlowBlock
        public bool TryReceive(Predicate<List<Channel>> filter, out List<Channel> item)
        {
            return _innerBuffer.TryReceive(filter, out item);
        }

        public bool TryReceiveAll(out IList<List<Channel>> items)
        {
            return _innerBuffer.TryReceiveAll(out items);
        }

        public IDisposable LinkTo(ITargetBlock<List<Channel>> target, DataflowLinkOptions linkOptions)
        {
            return _innerBuffer.LinkTo(target, linkOptions);
        }

        public List<Channel> ConsumeMessage(DataflowMessageHeader messageHeader, ITargetBlock<List<Channel>> target, out bool messageConsumed)
        {
            return ((IReceivableSourceBlock<List<Channel>>)_innerBuffer).ConsumeMessage(messageHeader, target, out messageConsumed);
        }

        public bool ReserveMessage(DataflowMessageHeader messageHeader, ITargetBlock<List<Channel>> target)
        {
            return ((IReceivableSourceBlock<List<Channel>>)_innerBuffer).ReserveMessage(messageHeader, target);
        }

        public void ReleaseReservation(DataflowMessageHeader messageHeader, ITargetBlock<List<Channel>> target)
        {
            ((IReceivableSourceBlock<List<Channel>>)_innerBuffer).ReleaseReservation(messageHeader, target);
        }

        public DataflowMessageStatus OfferMessage(DataflowMessageHeader messageHeader, List<Channel> messageValue, ISourceBlock<List<Channel>> source, bool consumeToAccept)
        {
            return ((ITargetBlock<List<Channel>>)_innerBuffer).OfferMessage(messageHeader, messageValue, source, consumeToAccept);
        }

        public void Complete()
        {
            _innerBuffer.Complete();
        }

        public void Fault(Exception exception)
        {
            ((IReceivableSourceBlock<List<Channel>>)_innerBuffer).Fault(exception);
        }
        #endregion
        /// <summary>
        /// Флаг для сохранения лайт барьера
        /// </summary>
        public void LightBarrierReached(double barrierCoord)
        {
            _barriedCoord = barrierCoord;
            _saveStopCoord = true;
        }

        public double GetLastCoord()
        {
            double max = 0;
            foreach (var zed in _channelPoints)
            {
                foreach (var channel in zed)
                {
                    foreach (var gate in channel)
                    {
                        if (gate.Count > 0)
                        {
                            max = Math.Max(max, gate[gate.Count - 1].X);
                        }
                    }
                }
            }
            return max;
        }

        /// <summary>
        /// Проверяет потерю точек
        /// </summary>
        /// <param name="step">Допустимое растояние между N и N+1</param>
        /// <returns>True если растояние между соседними точками менее step</returns>
        public bool CheckCoordMissing(double step)
        {
            bool res = true;
            for (int zed = 0; zed < _channelPoints.Length; zed++)
            {
                for (int channel = 0; channel < _channelPoints[zed].Length; channel++)
                {
                    for (int gate = 0; gate < _channelPoints[zed][channel].Length; gate++)
                    {
                        for (int i = 0; i < _channelPoints[zed][channel][gate].Count - 1; i++)
                        {
                            if (_channelPoints[zed][channel][gate][i + 1].X - _channelPoints[zed][channel][gate][i].X > step)
                            {
                                res = false;
                                goto end;
                            }
                        }
                    }
                }
            }
            end:
            return res;
        }

        /// <summary>
        /// Добавление данных для отрисовки без использования Converter модуля
        /// </summary>
        /// <param name="data">Список точек для отрисовки</param>
        public void PostData(List<Channel> data)
        {
            _innerBuffer.Post(data);
        }
        #endregion
        #region Private
        /// <summary>
        /// Инициализация настроек
        /// </summary>
        /// <param name="settings">Настройки отрисовки</param>
        /// <param name="zedControls">Контролы</param>
        private void InitZedControls(DrawSettings[] settings, ZedGraphControl[] zedControls)
        {
            for (int i = 0; i < zedControls.Length; i++)
            {
                GraphPane pane = zedControls[i].GraphPane;
                pane.CurveList.Clear();

                for (int channel = 0; channel < _channelPoints[i].Length; channel++)
                {
                    //Гейты
                    LineItem myCurve;
                    for (int gate = 0; gate < _channelPoints[i][channel].Length; gate++)
                    {
                        myCurve = pane.AddCurve(pane.Title.Text, _channelPoints[i][channel][gate], settings[i].ChannelSettings[channel].GateColors[gate]);
                        // Тип заполнения - сплошная заливка
                        myCurve.Symbol.Fill.Type = FillType.Solid;
                        // Размер ромбиков
                        myCurve.Symbol.Size = 1;
                    }
                    //DeadZone start
                    myCurve = pane.AddCurve(pane.Title.Text, new PointPairList()
                    {
                        new PointPair( _channelsDeadZoneStartOffset/*[settings[i].ChannelSettings[channel].ChannelIdx]*/, pane.YAxis.Scale.Min),
                        new PointPair( _channelsDeadZoneStartOffset/*[settings[i].ChannelSettings[channel].ChannelIdx]*/, pane.YAxis.Scale.Max)
                    },
                    Color.White);
                }



                pane.AxisChange();
            }
        }
        /// <summary>
        /// Функция отрисовки
        /// </summary>
        /// <param name="channels">Список точек</param>
        private void Draw(List<Channel> channels)
        {
            //Отрисовка лайт барьера
            if (_saveStopCoord && !_stopCoordSaved)
            {

                DrawDeadZoneEndCurve(_barriedCoord - _channelsProbeOffset[channels[0].ChannelId], _zedControls);
                _stopCoordSaved = true;
            }
            double maxX = _zedControls[0].GraphPane.XAxis.Scale.Max; //TODO fix [0] 
            foreach (var channel in channels)
            {
                for (int gate = 0; gate < channel.Gates.Count; gate++)
                {
                    for (int dataPoint = 0; dataPoint < channel.Gates[gate].GatePoints.Count; dataPoint++)
                    {
                        PointPair point = channel.Gates[gate].GatePoints[dataPoint];
                        point.X -= _channelsProbeOffset[channel.ChannelId];
                        //Расширение массива точек
                        if (point.X > maxX)
                        {
                            maxX *= 2;
                            foreach (var zed in _zedControls)
                            {
                                //zed.GraphPane.XAxis.Scale.Max = maxX * 2;
                                zed.GraphPane.XAxis.Scale.Max *= 2;
                                zed.AxisChange();
                            }
                        }
                        //Проверка что мы уже дошли до объекта контроля
                        if (point.X >= 0)
                        {
                            _channelToPointArr[channel.ChannelId][gate].AddPoint(point);
                        }
                    }
                }
            }
        }
        /// <summary>
        /// Отрисовка DeadZone end метки
        /// </summary>
        /// <param name="xCoord">Х координата метки</param>
        /// <param name="zedC">Список контролов в которых будем рисовать</param>
        private void DrawDeadZoneEndCurve(double xCoord, ZedGraphControl[] zedC)
        {

            foreach (var control in zedC)
            {
                GraphPane pane = control.GraphPane;

                pane.AddCurve(pane.Title.Text, new PointPairList()
                    {
                        new PointPair( xCoord, pane.YAxis.Scale.Min),
                        new PointPair( xCoord, pane.YAxis.Scale.Max)
                    },
                 Color.White);
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
        #endregion

    }
    /// <summary>
    /// Клас настроек отрсовки
    /// </summary>
    public class DrawSettings
    {
        private ChannelSettings[] _channelSettings;

        public ChannelSettings[] ChannelSettings
        {
            get
            {
                return _channelSettings;
            }
        }

        public DrawSettings(ChannelSettings[] channelSettings)
        {
            for (int i = 0; i < channelSettings.Length; i++)
            {
                if (channelSettings[i].ChannelIdx < 0)
                    throw new ArgumentException("Channel idx is not set!");
                if (channelSettings[i].ChannelCaption == null)
                    throw new ArgumentNullException("Channel caption is not set!");
                if (channelSettings[i].GateColors == null)
                    throw new ArgumentNullException("Channel gate colors is null!");
                if (channelSettings[i].GateColors.Count == 0)
                    throw new ArgumentNullException("Channel gate colors lenght is zero!");
            }
            _channelSettings = channelSettings;
        }

        //public Dictionary<int, double> GetChannelsStartOffset()
        //{
        //    Dictionary<int, double> res = new Dictionary<int, double>();
        //    foreach (var chan in _channelSettings)
        //    {
        //        res.Add(chan.ChannelIdx, chan.StartOffset);
        //    }
        //    return res;
        //}
        //public Dictionary<int, double> GetChannelsDeadZoneStartOffset()
        //{
        //    Dictionary<int, double> res = new Dictionary<int, double>();
        //    foreach (var chan in _channelSettings)
        //    {
        //        res.Add(chan.ChannelIdx, chan.DeadZoneStartOffset);
        //    }
        //    return res;
        //}
        //public Dictionary<int, double> GetChannelsDeadZoneEndOffset()
        //{
        //    Dictionary<int, double> res = new Dictionary<int, double>();
        //    foreach (var chan in _channelSettings)
        //    {
        //        res.Add(chan.ChannelIdx, chan.DeadZoneEndOffset);
        //    }
        //    return res;
        //}
    }

    /// <summary>
    /// Настройки конкретного ID для отрисовки
    /// </summary>
    public class ChannelSettings
    {
        private int _channelIdx = 0;
        private string _channelCaption = "";
        private int _pointCount = 0;
        private List<Color> _gateColor;
        private double _startOffset = 0;
        private double _deadZoneStartOffset = 0;
        private double _deadZoneEndOffset = 999999999;
        public int ChannelIdx
        {
            get
            {
                return _channelIdx;
            }
        }

        public string ChannelCaption
        {
            get
            {
                return _channelCaption;
            }
        }
        public double StartOffset
        {
            get
            {
                return _startOffset;
            }
        }
        public double DeadZoneStartOffset
        {
            get
            {
                return _deadZoneStartOffset;
            }
        }
        public double DeadZoneEndOffset
        {
            get
            {
                return _deadZoneEndOffset;
            }
        }
        public int PointCount
        {
            get
            {
                return _pointCount;
            }
        }
        public List<Color> GateColors
        {
            get
            {
                return _gateColor;
            }
        }

        public ChannelSettings(int channelIdx, string channelCaption, int pointCount, List<Color> gateColors, int startOffset, int deadZoneStartOffset, int deadZoneEndOffset)
        {
            _channelIdx = channelIdx;
            _channelCaption = channelCaption;
            _pointCount = pointCount;
            _gateColor = gateColors;
            _startOffset = startOffset;
            _deadZoneStartOffset = deadZoneStartOffset;
            _deadZoneEndOffset = deadZoneEndOffset;
        }
    }
}
