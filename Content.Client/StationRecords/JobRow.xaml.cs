using Robust.Client.AutoGenerated;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.XAML;
namespace Content.Client.StationRecords;

[GenerateTypedNameReferences]

public sealed partial class JobRow : PanelContainer
{
    public string? Job;
    public JobRow()
    {
        RobustXamlLoader.Load(this);
    }
}
