using System;
using System.Collections.Generic;
using System.Text;
using OpenMetaverse;

namespace EstateManagementModule
{
    public interface IRegionEstateModification
    {
        void Initialise(string connectstring);
        void SetRegionsEstate(UUID region, int estate);
        Dictionary<int, string> GetEstates();
        //void CreateEstate();
        //void RemoveEstate();
    }
}
