using System.Collections.ObjectModel;
using KfChatDotNetGui.Models;
using ReactiveUI;

namespace KfChatDotNetGui.ViewModels;

public class RoomSettingsWindowViewModel : ViewModelBase
{

    private ObservableCollection<RoomSettingsModel.RoomList> _roomList = new()
    {
        new RoomSettingsModel.RoomList {Id = 1, Name = "General"}
    };
    
    public ObservableCollection<RoomSettingsModel.RoomList> RoomList
    {
        get => _roomList;
        set => this.RaiseAndSetIfChanged(ref _roomList, value);
    }
}