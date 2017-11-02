using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NCE.UTscanner.Processing.Drawing
{
    /// <summary>
    /// Список политик роста массива точек
    /// </summary>
    public enum PointOverflowPolicy
    {
        /// <summary>
        /// Список расти не будет, точки мне массима игнорируются
        /// </summary>
        Ignore = 0,
        /// <summary>
        /// При выходе за предел массива выдаст исключение
        /// </summary>
        Allow,
        /// <summary>
        /// Очищает массив и ничанает с 0
        /// </summary>
        ClearAndStartFromZero,
        /// <summary>
        /// Увеливает массив в 2 раза при нехватке места
        /// </summary>
        GrowTwice,
    }
    /// <summary>
    /// Список состояний при добавлении новой точки
    /// </summary>
    public enum AddedPointState
    {
        /// <summary>
        /// Точка добавленна успешно
        /// </summary>
        Succes = 0,
        /// <summary>
        /// Точка приогнорирована
        /// </summary>
        Ignored,
        /// <summary>
        /// Список вырос в 2 раза для добавления точки
        /// </summary>
        GrowedTwice,
        /// <summary>
        /// Список был очищен и новая точка добавленна в начало
        /// </summary>
        StartedFromZero,

    }
}
