using Content.Client.UserInterface.Controls;
using Content.Shared.Xenobiology.Components;
using Content.Shared.Xenobiology.UI;
using Robust.Client.AutoGenerated;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Utility;

namespace Content.Client._Wega.Xenobiology.UI;

[GenerateTypedNameReferences]
public sealed partial class SlimeAnalyzerWindow : FancyWindow
{
    [Dependency] private readonly IEntityManager _entityManager = default!;

    public SlimeAnalyzerWindow()
    {
        RobustXamlLoader.Load(this);
        IoCManager.InjectDependencies(this);
    }

    public void Populate(SlimeAnalyzerScannedUserMessage msg)
    {
        var target = _entityManager.GetEntity(msg.TargetEntity);
        if (!_entityManager.HasComponent<SlimeGrowthComponent>(target))
        {
            NoSlimeDataText.Visible = true;
            SlimeDataContainer.Visible = false;
            return;
        }

        NoSlimeDataText.Visible = false;
        SlimeDataContainer.Visible = true;

        // Basic info
        SlimeSpriteView.SetEntity(target);
        var slimeTypeMessage = new FormattedMessage();
        slimeTypeMessage.AddText(Loc.GetString($"slime-analyzer-type-{msg.SlimeType.ToString().ToLower()}"));
        SlimeTypeLabel.SetMessage(slimeTypeMessage);
        var stage = Loc.GetString($"slime-analyzer-stage-{msg.GrowthStage.ToString().ToLower()}");
        SlimeStageLabel.Text = Loc.GetString("slime-analyzer-stage", ("stage", stage));

        // Status
        BehaviorStateLabel.Text = Loc.GetString($"slime-analyzer-behavior-{msg.BehaviorState.ToString().ToLower()}");
        HungerBar.Value = msg.Hunger / msg.MaxHunger;
        HungerText.Text = $"{msg.Hunger:F1}/{msg.MaxHunger:F1}";
        MutationChanceLabel.Text = $"{msg.MutationChance * 100:F1}%";
        RainbowChanceLabel.Text = $"{msg.RainbowChance * 100:F1}%";

        // Mutations
        MutationsList.RemoveAllChildren();
        if (msg.PossibleMutations != null)
        {
            foreach (var (type, weight) in msg.PossibleMutations)
            {
                MutationsList.AddChild(new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Horizontal,
                    Children =
                    {
                        new Label { Text = "· ", Margin = new Thickness(5, 0, 0, 0) },
                        new Label { Text = Loc.GetString($"slime-analyzer-type-{type.ToString().ToLower()}"), HorizontalExpand = true },
                        new Label { Text = $"{weight:F1}x", Margin = new Thickness(0, 0, 5, 0) }
                    }
                });
            }
        }
    }
}
