using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NCE.Processing.Drawnig
{
    public class CombineSettings
    {
        private List<CombineRule> _rules;

        public List<CombineRule> Rules { get { return _rules; } }

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
        private int _displayId;
        /// <summary>
        /// Битовая маска гейтов которые нужно объединять
        /// </summary>
        public int GateCombineMask { get { return _gateCombineMask; } }
        /// <summary>
        /// Список каналов которые объединяются
        /// </summary>
        public List<int> ChannelsIds { get { return _channelsIds; } }

        public int GatesCount { get { return _gatesCount; } }
        public int DisplayId { get { return _displayId; } }

        public CombineRule(List<int> combineChannelsIds, int gateMask, int displayId)
        {
            _channelsIds = combineChannelsIds;
            _gateCombineMask = gateMask;
            _displayId = displayId;
            _gatesCount = gateMask / 2 + gateMask % 2;
        }
    }
}
