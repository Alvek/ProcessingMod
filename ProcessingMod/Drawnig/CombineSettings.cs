using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NCE.UTscanner.Processing.Drawnig
{
    public class CombineSettings
    {
        public List<CombineRule> Rules { get; }
    }

    public class CombineRule
    {
        /// <summary>
        /// Битовая маска гейтов которые нужно объединять
        /// </summary>
        public int GateCombineMask { get { return _gateCombineMask; } }
        /// <summary>
        /// Список каналов которые объединяются
        /// </summary>
        public List<int> ChannelsIds { get { return _channelsIds; } }

        private List<int> _channelsIds;
        private int _gateCombineMask;
    }
}
