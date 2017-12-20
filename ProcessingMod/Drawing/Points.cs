using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZedGraph;

namespace NCE.Processing.Drawing
{
    /// <summary>
    /// Базовый клас для точек отрисовки
    /// </summary>
    public abstract class BasePoints
    {
        public abstract void ClearPoints();
    }

    /// <summary>
    /// Реализация линейного массива точек отрисовки
    /// </summary>
    public class LinearBscanPoints : BasePoints, IPointList
    {
        /// <summary>
        /// Массив точек отрисовки
        /// </summary>
        private PointPair[] _data;
        /// <summary>
        /// Объект синхронизации
        /// </summary>
        private object _syncObj = new object();
        /// <summary>
        /// Индекс новой точки
        /// </summary>
        private int _idx = 0;
        /// <summary>
        /// Политика роста массива
        /// </summary>
        private PointOverflowPolicy _policy;
        /// <summary>
        /// Инициализация массива точек
        /// </summary>
        /// <param name="count">Начальное количество точек</param>
        /// <param name="policy">Политика роста массива точек</param>
        public LinearBscanPoints(int count, PointOverflowPolicy policy)
        {
            _policy = policy;
            _data = new PointPair[count];
            for (int i = 0; i < _data.Length; i++)
            {
                _data[i] = new PointPair();
            }
        }
        /// <summary>
        /// Используется для внутреннего клонирования массива точек
        /// </summary>
        /// <param name="data">Список точек</param>
        protected LinearBscanPoints(PointPair[] data)
        {
            //_data = data;
            _data = new PointPair[data.Length];
            Array.Copy(data, _data, data.Length);
        }

        /// <summary>
        /// Точка отрисовки
        /// </summary>
        /// <param name="index">Индекс точки</param>
        /// <returns></returns>
        public PointPair this[int index]
        {
            get
            {
                var localIdx = _idx - 1;//сохранение индекса последней точки на случай мульти тред приложения

                //Проверка что мы запрашиваем еще не заполненную точку - первую или точку между последней заполненной и концом массива
                if (localIdx > -1 && index > localIdx)
                {
                    return _data[localIdx];
                }
                else
                {
                    return _data[index];
                }
            }
            set
            {
                if (value == null)
                    throw new ArgumentNullException("PointPair can't be null!");

                PointPair local = null;
                //Запись точки с учетом возможной мультипоточной записи точек
                do
                {
                    local = _data[index];
                }
                while (Interlocked.CompareExchange<PointPair>(ref _data[index], value, local) != local);

            }
        }
        
        /// <summary>
        /// Добавление новой точки
        /// </summary>
        /// <param name="point">Точка отрисовки</param>
        /// <returns>Состояние после добавление точки</returns>
        public AddedPointState AddPoint(PointPair point)
        {
            AddedPointState res;
            int localIdx;
            if (_policy == PointOverflowPolicy.Allow)
            {
                localIdx = _idx++;
                _data[localIdx] = point;
                res = AddedPointState.Succes;
            }
            else if (_policy == PointOverflowPolicy.Ignore)
            {
                localIdx = _idx++;
                if (localIdx < _data.Length)
                {
                    _data[localIdx] = point;
                    res = AddedPointState.Succes;
                }
                else
                {
                    res = AddedPointState.Ignored;
                }
            }
            else if (_policy == PointOverflowPolicy.ClearAndStartFromZero)
            {
                if (_idx + 1 > _data.Length)
                {
                    this.ClearPoints();
                    _idx = 0;
                    localIdx = 0;
                    res = AddedPointState.StartedFromZero;
                }
                else
                {
                    localIdx = _idx++;
                    res = AddedPointState.Succes;
                }
                _data[localIdx] = point;
            }
            else if (_policy == PointOverflowPolicy.GrowTwice)
            {
                res = AddedPointState.Succes;
                //Проверка необходимости увеличения массива
                if (_idx + 1 > _data.Length)
                {
                    //Блокирование для гаранантии правильного добавления точек в мультипоточном приложении
                    lock (_syncObj)
                    {
                        //Запасная проверка что массив не был увеличен во время блокировки
                        if (_idx + 1 > _data.Length)
                        {
                            PointPair[] dataTemp = new PointPair[_data.Length];
                            Array.Copy(_data, dataTemp, _data.Length);
                            _data = new PointPair[_data.Length * 2];
                            Array.Copy(dataTemp, _data, dataTemp.Length);
                            res = AddedPointState.GrowedTwice;
                        }
                    }
                }
                localIdx = _idx++;
                _data[localIdx] = point;
            }
            else
                throw new NotImplementedException(string.Format("Point overflow policy {0} is not implemented!", _policy.ToString()));

            return res;
        }

        /// <summary>
        /// Количество точек
        /// </summary>
        public int Count
        {
            get
            {
                return _idx;
            }
        }


        /// <summary>
        /// Клонирование массива
        /// </summary>
        public object Clone()
        {
            lock (_syncObj)
                return new LinearBscanPoints(_data);
        }

        /// <summary>
        /// Очистка массива
        /// </summary>
        public override void ClearPoints()
        {
            lock (_syncObj)
            {
                _data = new PointPair[_data.Length];
                for (int i = 0; i < _data.Length; i++)
                    _data[i] = new PointPair();
                _idx = 0;
            }
        }
    }

    public class ClassicAscanPoints : IPointList
    {
        /// <summary>
        /// Массив точек отрисовки
        /// </summary>
        private PointPair[] _data;

        public ClassicAscanPoints()
        {
            _data = new PointPair[] { new PointPair() };
        }

        public ClassicAscanPoints(PointPair[] points)
        {
            _data = points;
        }

        /// <summary>
        /// Точка отрисовки
        /// </summary>
        /// <param name="index">Индекс точки</param>
        /// <returns></returns>
        public PointPair this[int index]
        {
            get
            {
                return _data[index];
            }          
        }

        /// <summary>
        /// Количество точек
        /// </summary>
        public int Count
        {
            get
            {
                return _data.Length;
            }
        }

        public void UpdatePoint(PointPair[] points)
        {
            _data = points;
        }

        /// <summary>
        /// Клонирование массива
        /// </summary>
        public object Clone()
        {
            PointPair[] data = new PointPair[_data.Length];
            Array.Copy(_data, data, data.Length);
            return new ClassicAscanPoints(data);            
        }
    }
}
