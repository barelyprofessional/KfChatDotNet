using System.Collections.Generic;

namespace KfChatDotNetGui.Models;

public class RoomSettingsModel
{
    public class RoomList
    {
        public string Name { get; set; }
        public int Id { get; set; }
    }

    public List<RoomList> Rooms { get; set; }
}