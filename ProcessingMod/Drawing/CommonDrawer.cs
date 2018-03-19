using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NCE.Processing.Drawing
{
    public abstract class CommonChannelSettings
    {
        /// <summary>
        /// Id канала board\channel
        /// </summary>
        public abstract int ChannelIdx { get; }
        /// <summary>
        /// Название канала, для curve которые создаем при инициализации
        /// </summary>
        public abstract string ChannelCaption { get; }
        /// <summary>
        /// Смещение датчика
        /// </summary>
        public abstract double StartOffset { get; }
        /// <summary>
        /// Количество точек, с него создаем массив
        /// </summary>
        public abstract int PointCount { get; }
        /// <summary>
        /// Цвета curve
        /// </summary>
        public abstract List<Color> GateColors { get; }
    }
    

    /// <summary>
    /// Настройки конкретного ID для отрисовки
    /// </summary>
    public class ChannelSettings : CommonChannelSettings
    {
        private int _channelIdx = 0;
        private string _channelCaption = "";
        private int _pointCount = 0;
        private List<Color> _gateColor;
        private double _startOffset = 0;
        //private double _deadZoneStartOffset = 0;
        //private double _deadZoneEndOffset = 999999999;
        public override int ChannelIdx
        {
            get
            {
                return _channelIdx;
            }
        }
        public override string ChannelCaption
        {
            get
            {
                return _channelCaption;
            }
        }
        public override double StartOffset
        {
            get
            {
                return _startOffset;
            }
        }
        //public double DeadZoneStartOffset
        //{
        //    get
        //    {
        //        return _deadZoneStartOffset;
        //    }
        //}
        //public double DeadZoneEndOffset
        //{
        //    get
        //    {
        //        return _deadZoneEndOffset;
        //    }
        //}
        public override int PointCount
        {
            get
            {
                return _pointCount;
            }
        }
        public override List<Color> GateColors
        {
            get
            {
                return _gateColor;
            }
        }

        public ChannelSettings(int channelIdx, string channelCaption, int pointCount, List<Color> gateColors, int startOffset)//, int deadZoneStartOffset, int deadZoneEndOffset)
        {
            _channelIdx = channelIdx;
            _channelCaption = channelCaption;
            _pointCount = pointCount;
            _gateColor = gateColors;
            _startOffset = startOffset;
            //_deadZoneStartOffset = deadZoneStartOffset;
            //_deadZoneEndOffset = deadZoneEndOffset;
        }
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
                    throw new ArgumentException("channelSettings", "Channel idx is not set!");
                if (channelSettings[i].ChannelCaption == null)
                    throw new ArgumentNullException("channelSettings", "Channel caption is not set!");
                if (channelSettings[i].GateColors == null)
                    throw new ArgumentNullException("channelSettings", "Channel gate colors is null!");
                if (channelSettings[i].GateColors.Count == 0)
                    throw new ArgumentNullException("channelSettings", "Channel gate colors lenght is zero!");
            }
            _channelSettings = channelSettings;
        }        
    }

    public enum AscanConverterWorkingMod
    {
        Snapshot = 1,
        SummCollecting,
    }
}
