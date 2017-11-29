using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NCE.UTscanner.Processing.Drawnig
{
    public class CombineSettings
    {
        private List<CombineRule> _rules;

        public List<CombineRule> Rules { get => _rules; }

        public CombineSettings()
        {
            _rules = new List<CombineRule>();
        }
    }

    public class CombineRule
    {
        private List<int> _channelsIds;
        private int _gateCombineMask;
        private int _gatesCount;

        /// <summary>
        /// Битовая маска гейтов которые нужно объединять
        /// </summary>
        public int GateCombineMask { get { return _gateCombineMask; } }
        /// <summary>
        /// Список каналов которые объединяются
        /// </summary>
        public List<int> ChannelsIds { get { return _channelsIds; } }

        public int GatesCount { get => _gatesCount; }

        public CombineRule(List<int> combineChannelsIds, int gateMask)
        {
            _channelsIds = combineChannelsIds;
            _gateCombineMask = gateMask;

            _gatesCount = gateMask / 2 + gateMask % 2;
        }
    }
}
