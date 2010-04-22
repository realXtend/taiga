using System;
namespace EstateManagementModule
{
    interface IEstateRegionManager
    {
        void HandleAddRegionBan(string module, string[] cmd);
        void HandleAddToRegionAccessList(string module, string[] cmd);
        void HandleRemoveFromRegionAccessList(string module, string[] cmd);
        void HandleRemoveRegionBan(string module, string[] cmd);
        void HandleSetCurrentEstateID(string module, string[] cmd);
        void HandleSetRegionPrivate(string module, string[] cmd);
        void HandleSetRegionPublic(string module, string[] cmd);
        void HandleShowEstateBanList(string module, string[] cmd);
        void HandleShowCurrentEstateID(string module, string[] cmd);
        void HandleShowEstateAccessList(string module, string[] cmd);
    }
}
