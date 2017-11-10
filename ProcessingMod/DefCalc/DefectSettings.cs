using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace NCE.Processing
{
    /// <summary>
    /// Базовый клас с обязательными настройками фильтрации
    /// </summary>
    public abstract class DefCalcSettings
    {
        public abstract double MinDefectLenght { get; }
        public abstract double MinDefAmp { get; }
    }

    /// <summary>
    /// Реализация настроек для линейного подсчета
    /// </summary>
    [DataContract]
    public class LinearDefCalcSettings : DefCalcSettings
    {
        [DataMember]
        private double _minDefectLenght = 0;
        [DataMember]
        private double _minDefectAmp = 0;
        public override double MinDefectLenght
        {
            get
            {
                return _minDefectLenght;
            }
        }
        public override double MinDefAmp
        {
            get
            {
                return _minDefectAmp;
            }
        }

        public LinearDefCalcSettings(double minLenght, double minAmp)
        {
            _minDefectLenght = minLenght;
            _minDefectAmp = minAmp;
        }
    }
}