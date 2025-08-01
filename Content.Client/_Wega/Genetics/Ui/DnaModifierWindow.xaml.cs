using Content.Client.Stylesheets;
using Content.Client.UserInterface.Controls;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Genetics;
using Content.Shared.Genetics.Systems;
using Content.Shared.Genetics.UI;
using Robust.Client.AutoGenerated;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Wega.Genetics.Ui;

[GenerateTypedNameReferences]
public sealed partial class DnaModifierWindow : FancyWindow
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IEntityNetworkManager _entNetworkManager = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    private static TimeSpan? _releverationsButtonCooldown;
    private static TimeSpan? _releveration1ButtonCooldown;
    private static TimeSpan? _releveration2ButtonCooldown;
    private TimeSpan? _injectorCooldown;
    private TimeSpan? _subjectInjectCooldown;
    private ModifyButton? _activeButtonUi = null;
    private ModifyButton? _activeButtonSe = null;
    private bool _initializedUi = false;
    private bool _updateUi = false;
    private bool _initializedSe = false;
    private bool _updateSe = false;
    private bool _initializedBuffer = false;
    private bool _updateBuffer = false;
    private NetEntity _console;

    public DnaModifierWindow()
    {
        RobustXamlLoader.Load(this);
        IoCManager.InjectDependencies(this);

        Tabs.SetTabTitle(0, Loc.GetString("dna-modifier-tab-ui"));
        Tabs.SetTabTitle(1, Loc.GetString("dna-modifier-tab-se"));
        Tabs.SetTabTitle(2, Loc.GetString("dna-modifier-tab-transfer"));
        Tabs.SetTabTitle(3, Loc.GetString("dna-modifier-tab-rejuvenator"));

        Tabs.OnTabChanged += OnTabChanged;

        RadiationTarget.OnValueChanged += OnSpinBoxValueChanged;
        RadiationIntensity.OnValueChanged += OnSpinBoxValueChanged;
        RadiationDuration.OnValueChanged += OnSpinBoxValueChanged;

        UpdateRadiationBoxVisibility(Tabs.CurrentTab);
        ReleverationsButton.OnPressed += _ =>
        {
            if (Tabs.CurrentTab == 0) _updateUi = true;
            else if (Tabs.CurrentTab == 1) _updateSe = true;

            _entNetworkManager.SendSystemNetworkMessage(
                new DnaModifierConsoleReleverationsEvent(_console, Tabs.CurrentTab, RadiationIntensity.Value, RadiationDuration.Value));
            _entNetworkManager.SendSystemNetworkMessage(new DnaModifierUpdateEvent(_console));

            ReleverationsButton.Disabled = true;
            _releverationsButtonCooldown = _gameTiming.CurTime + TimeSpan.FromSeconds(RadiationDuration.Value);
        };

        Releveration1Button.OnPressed += _ => OnButtonRadiationUiPressed();
        Releveration2Button.OnPressed += _ => OnButtonRadiationSePressed();

        SubjectUI1.OnPressed += _ => OnButtonServerSavedPressed(1, 1);
        SubjectUISE1.OnPressed += _ => OnButtonServerSavedPressed(1, 2);
        SubjectSE1.OnPressed += _ => OnButtonServerSavedPressed(1, 3);
        SubjectUI2.OnPressed += _ => OnButtonServerSavedPressed(2, 1);
        SubjectUISE2.OnPressed += _ => OnButtonServerSavedPressed(2, 2);
        SubjectSE2.OnPressed += _ => OnButtonServerSavedPressed(2, 3);
        SubjectUI3.OnPressed += _ => OnButtonServerSavedPressed(3, 1);
        SubjectUISE3.OnPressed += _ => OnButtonServerSavedPressed(3, 2);
        SubjectSE3.OnPressed += _ => OnButtonServerSavedPressed(3, 3);

        ClearButton1.OnPressed += _ => OnClearBufferPressed(1);
        ClearButton2.OnPressed += _ => OnClearBufferPressed(2);
        ClearButton3.OnPressed += _ => OnClearBufferPressed(3);
        RenameButton1.OnPressed += _ => OnRenameBufferPressed(1);
        RenameButton2.OnPressed += _ => OnRenameBufferPressed(2);
        RenameButton3.OnPressed += _ => OnRenameBufferPressed(3);
        ExportButton1.OnPressed += _ => OnExportOnDiskPressed(1);
        ExportButton2.OnPressed += _ => OnExportOnDiskPressed(2);
        ExportButton3.OnPressed += _ => OnExportOnDiskPressed(3);
    }

    private DnaModifierBoundUserInterfaceState? _lastUpdate;
    public void UpdateState(DnaModifierBoundUserInterfaceState state)
    {
        _lastUpdate = state;
        _console = state.Console;
        var console = _entManager.GetEntity(_console);

        UpdateCooldowns();

        // Upper state
        EjectButton.OnPressed += _ => _entNetworkManager.SendSystemNetworkMessage(
            new DnaModifierConsoleEjectEvent(_console));

        if (!string.IsNullOrWhiteSpace(state.ScannerBodyInfo))
        {
            NameLabel.Text = state.ScannerBodyInfo;
            StatusLabel.Text = state.ScannerBodyStatus;
            EjectButton.Disabled = false;
        }
        else
        {
            NameLabel.Text = Loc.GetString("dna-modifier-no-data");
            StatusLabel.Text = Loc.GetString("dna-modifier-no-data");
            EjectButton.Disabled = true;

            DisabledSubjectDiskBlock(null);
        }

        HealthBar.Value = state.ScannerBodyHealth;
        RadiationBar.Value = state.ScannerBodyRadiation;

        UpdateHealthBarColor(state.ScannerBodyHealth);

        UniqueEnzymesLabel.Text = string.IsNullOrWhiteSpace(state.ScannerBodyDna)
            ? Loc.GetString("dna-modifier-no-data")
            : state.ScannerBodyDna;

        // Lower state

        _injectorCooldown = state.InjectorCooldownRemaining > TimeSpan.Zero
            ? _gameTiming.CurTime + state.InjectorCooldownRemaining
            : null;

        _subjectInjectCooldown = state.SubjectInjectCooldownRemaining > TimeSpan.Zero
            ? _gameTiming.CurTime + state.SubjectInjectCooldownRemaining
            : null;

        // U.I.
        if (state.Unique != null && !_initializedUi)
        {
            UiPanel.Visible = true;
            UiContainer.RemoveAllChildren();
            InitilizeUniqueIdentifiers(state.Unique);
        }
        else if (state.Unique == null && _initializedUi)
        {
            _updateUi = false;
            _initializedUi = false;
            _activeButtonUi = null;
            UiContainer.RemoveAllChildren();
        }
        else if (state.Unique != null && _updateUi)
        {
            _updateUi = false;
            UiPanel.Visible = true;
            _initializedUi = true;
            _activeButtonUi = null;
            UiContainer.RemoveAllChildren();
            InitilizeUniqueIdentifiers(state.Unique);
        }

        // S.E.
        if (state.Enzymes != null && !_initializedSe)
        {
            SePanel.Visible = true;
            SeContainer.RemoveAllChildren();
            InitilizeStructuralEnzymes(state.Enzymes);
        }
        else if (state.Enzymes == null && _initializedSe)
        {
            _initializedSe = false;
            _activeButtonSe = null;
            SeContainer.RemoveAllChildren();
        }
        else if (state.Enzymes != null && _updateSe)
        {
            _updateSe = false;
            SePanel.Visible = true;
            _initializedSe = true;
            _activeButtonSe = null;
            SeContainer.RemoveAllChildren();
            InitilizeStructuralEnzymes(state.Enzymes);
        }

        // Buffer & Disk
        if (!_initializedBuffer)
        {
            BufferContainer1.RemoveAllChildren();
            BufferContainer2.RemoveAllChildren();
            BufferContainer3.RemoveAllChildren();
            InitializeBufferData(state.Buffers);
        }
        else if (_updateBuffer)
        {
            _updateBuffer = false;
            _initializedBuffer = false;
            BufferContainer1.RemoveAllChildren();
            BufferContainer2.RemoveAllChildren();
            BufferContainer3.RemoveAllChildren();
            InitializeBufferData(state.Buffers);
        }

        EjectButtonDisk.Disabled = state.HasDisk ? false : true;

        FromDisk1.Disabled = state.HasDisk ? false : true;
        FromDisk2.Disabled = state.HasDisk ? false : true;
        FromDisk3.Disabled = state.HasDisk ? false : true;

        FromDisk1.OnPressed += _ => OnExportFromDiskPressed(1);
        FromDisk2.OnPressed += _ => OnExportFromDiskPressed(2);
        FromDisk3.OnPressed += _ => OnExportFromDiskPressed(3);

        ClearButtonDisk.Disabled = state.HasDisk ? false : true;
        ClearButtonDisk.OnPressed += _ => _entNetworkManager.SendSystemNetworkMessage(
            new DnaModifierConsoleClearDiskEvent(_console));

        UpdateDiskContainer(state.Enzyme);

        // Rejuve
        RejuveContainer.RemoveAllChildren();
        CreateBeakerUI(RejuveContainer, state.InputContainerInfo);
        EjectRejuveButton.Disabled = state.ScannerHasBeaker ? false : true;
        EjectRejuveButton.OnPressed += _ => _entNetworkManager.SendSystemNetworkMessage(
            new DnaModifierConsoleEjectRejuveEvent(_console));
    }

    private void UpdateCooldowns()
    {
        var currentTime = _gameTiming.CurTime;
        if (_releverationsButtonCooldown.HasValue)
        {
            if (currentTime >= _releverationsButtonCooldown)
            {
                ReleverationsButton.Disabled = false;
                _releverationsButtonCooldown = null;
            }
            else
            {
                ReleverationsButton.Disabled = true;
            }
        }

        if (_releveration1ButtonCooldown.HasValue)
        {
            if (currentTime >= _releveration1ButtonCooldown)
            {
                Releveration1Button.Disabled = false;
                _releveration1ButtonCooldown = null;
            }
            else
            {
                Releveration1Button.Disabled = true;
            }
        }

        if (_releveration2ButtonCooldown.HasValue)
        {
            if (currentTime >= _releveration2ButtonCooldown)
            {
                Releveration2Button.Disabled = false;
                _releveration2ButtonCooldown = null;
            }
            else
            {
                Releveration2Button.Disabled = true;
            }
        }
    }

    private void UpdateHealthBarColor(float health)
    {
        if (HealthBar == null)
            return;

        var normalizedHealth = health / HealthBar.MaxValue;

        const float leftHue = 0.0f;
        const float middleHue = 0.066f;
        const float rightHue = 0.33f;
        const float saturation = 1.0f;
        const float value = 0.8f;
        const float alpha = 1.0f;

        const float leftSideSize = 0.5f;
        const float rightSideSize = 0.5f;

        float finalHue;
        if (normalizedHealth <= leftSideSize)
        {
            normalizedHealth /= leftSideSize;
            finalHue = MathHelper.Lerp(leftHue, middleHue, normalizedHealth);
        }
        else
        {
            normalizedHealth = (normalizedHealth - leftSideSize) / rightSideSize;
            finalHue = MathHelper.Lerp(middleHue, rightHue, normalizedHealth);
        }

        HealthBar.ForegroundStyleBoxOverride ??= new StyleBoxFlat();

        var foregroundStyleBoxOverride = (StyleBoxFlat)HealthBar.ForegroundStyleBoxOverride;
        foregroundStyleBoxOverride.BackgroundColor =
            Color.FromHsv(new Robust.Shared.Maths.Vector4(finalHue, saturation, value, alpha));
    }

    private void OnTabChanged(int tabIndex)
    {
        UpdateRadiationBoxVisibility(tabIndex);
    }

    private void UpdateRadiationBoxVisibility(int tabIndex)
    {
        RadiationBox.Visible = tabIndex == 0 || tabIndex == 1;
    }

    private void OnSpinBoxValueChanged(FloatSpinBox.FloatSpinBoxEventArgs args)
    {
        if (args.Value < 1)
        {
            args.Control.Value = 1;
        }
        else if (args.Value > 10)
        {
            args.Control.Value = 10;
        }
    }

    private void InitializeBufferData(Dictionary<int, EnzymeInfo?> buffers)
    {
        _initializedBuffer = true;

        for (int bufferIndex = 1; bufferIndex <= 3; bufferIndex++)
        {
            if (buffers.TryGetValue(bufferIndex, out var data) && data != null)
            {
                var bufferContainer = CreateBufferContainer(data, bufferIndex);
                var targetContainer = bufferIndex switch
                {
                    1 => BufferContainer1,
                    2 => BufferContainer2,
                    3 => BufferContainer3,
                    _ => null
                };

                if (targetContainer != null)
                {
                    targetContainer.AddChild(bufferContainer);
                    EnableBufferBlock(bufferIndex);
                    DisabledSubjectDiskBlock(bufferIndex);
                }
            }
            else
            {
                var bufferLabel = new Label { Text = Loc.GetString("dna-modifier-button-no-buffer") };
                var targetContainer = bufferIndex switch
                {
                    1 => BufferContainer1,
                    2 => BufferContainer2,
                    3 => BufferContainer3,
                    _ => null
                };

                targetContainer?.AddChild(bufferLabel);
            }
        }
    }

    private BoxContainer CreateBufferContainer(EnzymeInfo data, int bufferIndex)
    {
        var sampleName = data.SampleName;
        var bufferContainer = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };

        // Name Container
        var nameContainer = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        nameContainer.AddChild(new Label { Text = Loc.GetString("dna-modifier-label-sample-name"), MinWidth = 40 });
        nameContainer.AddChild(new Control { MinWidth = 25 });
        nameContainer.AddChild(new Label { Text = sampleName });

        // Data Container
        var dataContainer = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        dataContainer.AddChild(new Label { Text = Loc.GetString("dna-modifier-label-data"), MinWidth = 40 });
        dataContainer.AddChild(new Control { MinWidth = 25 });

        var dataLabel = new Label();
        bool hasUniqueIdentifiers = data.Identifier != null;
        bool hasStructuralEnzymes = data.Info != null && data.Info.Count > 0;

        dataLabel.Text = (hasUniqueIdentifiers, hasStructuralEnzymes) switch
        {
            (true, true) => Loc.GetString("dna-modifier-label-both-prototypes"),
            (true, false) => Loc.GetString("dna-modifier-label-unique-identifiers"),
            (false, true) => Loc.GetString("dna-modifier-label-structural-enzymes"),
            _ => Loc.GetString("dna-modifier-label-no-prototypes")
        };

        dataContainer.AddChild(dataLabel);

        // Buttons Container
        var buttonsContainer = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        buttonsContainer.AddChild(new Label { Text = Loc.GetString("dna-modifier-label-actions"), MinWidth = 40 });
        buttonsContainer.AddChild(new Control { MinWidth = 25 });

        // Injector Button
        var injectorButton = new Button
        {
            Name = "InjectorButton",
            Text = Loc.GetString("dna-modifier-button-injector"),
            Disabled = _injectorCooldown.HasValue && _gameTiming.CurTime < _injectorCooldown
        };
        injectorButton.OnPressed += _ => OnInjectorPressed(bufferIndex, injectorButton);
        buttonsContainer.AddChild(injectorButton);

        // Block Inject Button
        if (hasStructuralEnzymes)
        {
            var blockSelectButton = new OptionButton
            {
                MinWidth = 40,
                StyleClasses = { StyleNano.ButtonOpenRight }
            };
            for (int i = 1; i <= 55; i++)
            {
                blockSelectButton.AddItem($"{i}", i);
            }
            blockSelectButton.SelectId(1);
            blockSelectButton.OnItemSelected += args => blockSelectButton.SelectId(args.Id);

            var injectBlockButton = new Button
            {
                Text = Loc.GetString("dna-modifier-button-inject-block"),
                StyleClasses = { StyleNano.ButtonOpenLeft },
                Disabled = _injectorCooldown.HasValue && _gameTiming.CurTime < _injectorCooldown
            };
            injectBlockButton.OnPressed += _ => OnInjectBlockPressed(bufferIndex, injectBlockButton, blockSelectButton.SelectedId);

            buttonsContainer.AddChild(blockSelectButton);
            buttonsContainer.AddChild(injectBlockButton);
        }

        // Subject Inject Button
        var subjectInjectButton = new Button
        {
            Name = "SubjectInjectButton",
            Text = Loc.GetString("dna-modifier-button-subject-inject"),
            Disabled = _subjectInjectCooldown.HasValue && _gameTiming.CurTime < _subjectInjectCooldown
        };
        subjectInjectButton.OnPressed += _ => OnSubjectInjectPressed(bufferIndex, subjectInjectButton);
        buttonsContainer.AddChild(subjectInjectButton);

        // Assemble container
        bufferContainer.AddChild(nameContainer);
        bufferContainer.AddChild(dataContainer);
        bufferContainer.AddChild(buttonsContainer);

        return bufferContainer;
    }

    private void UpdateDiskContainer(EnzymeInfo? enzyme)
    {
        DiskContainer.RemoveAllChildren();
        if (enzyme == null)
        {
            DiskContainer.AddChild(new Label
            {
                Text = Loc.GetString("dna-modifier-no-disk")
            });
            return;
        }

        var bufferContainer = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };

        // Name Container
        var nameContainer = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        nameContainer.AddChild(new Label
        {
            Text = Loc.GetString("dna-modifier-label-sample-name"),
            MinWidth = 40
        });
        nameContainer.AddChild(new Control { MinWidth = 25 });
        nameContainer.AddChild(new Label
        {
            Text = enzyme.SampleName
        });

        // Data Container
        var dataContainer = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        dataContainer.AddChild(new Label
        {
            Text = Loc.GetString("dna-modifier-label-data"),
            MinWidth = 40
        });
        dataContainer.AddChild(new Control { MinWidth = 25 });

        var dataLabel = new Label();
        bool hasUniqueIdentifiers = enzyme.Identifier != null;
        bool hasStructuralEnzymes = enzyme.Info != null && enzyme.Info.Count > 0;

        dataLabel.Text = (hasUniqueIdentifiers, hasStructuralEnzymes) switch
        {
            (true, true) => Loc.GetString("dna-modifier-label-both-prototypes"),
            (true, false) => Loc.GetString("dna-modifier-label-unique-identifiers"),
            (false, true) => Loc.GetString("dna-modifier-label-structural-enzymes"),
            _ => Loc.GetString("dna-modifier-label-no-prototypes")
        };

        dataContainer.AddChild(dataLabel);

        // Assemble container
        bufferContainer.AddChild(nameContainer);
        bufferContainer.AddChild(dataContainer);
        DiskContainer.AddChild(bufferContainer);
    }

    private void OnButtonRadiationUiPressed()
    {
        if (_activeButtonUi != null)
        {
            _updateUi = true;
            _entNetworkManager.SendSystemNetworkMessage(
                new DnaModifierConsoleReleverationEvent(_console, 0, _activeButtonUi.Block, _activeButtonUi.Values, RadiationTarget.Value));
            _entNetworkManager.SendSystemNetworkMessage(new DnaModifierUpdateEvent(_console));

            Releveration1Button.Disabled = true;
            _releveration1ButtonCooldown = _gameTiming.CurTime + TimeSpan.FromSeconds(2);
        }
    }

    private void OnButtonRadiationSePressed()
    {
        if (_activeButtonSe != null)
        {
            _updateSe = true;
            _entNetworkManager.SendSystemNetworkMessage(
                new DnaModifierConsoleReleverationEvent(_console, 1, _activeButtonSe.Block, _activeButtonSe.Values, 1));
            _entNetworkManager.SendSystemNetworkMessage(new DnaModifierUpdateEvent(_console));

            Releveration2Button.Disabled = true;
            _releveration2ButtonCooldown = _gameTiming.CurTime + TimeSpan.FromSeconds(2);
        }
    }

    private void OnButtonServerSavedPressed(int section, int type)
    {
        _updateBuffer = true;
        _entNetworkManager.SendSystemNetworkMessage(
            new DnaModifierConsoleSaveServerEvent(_console, section, type));
    }

    private void OnInjectorPressed(int index, Button button)
    {
        _updateBuffer = true;
        button.Disabled = true;
        _entNetworkManager.SendSystemNetworkMessage(
            new DnaModifierConsoleInjectorEvent(_console, index));
    }

    private void OnInjectBlockPressed(int bufferIndex, Button button, int blockId)
    {
        _updateBuffer = true;
        button.Disabled = true;
        _entNetworkManager.SendSystemNetworkMessage(
            new DnaModifierInjectBlockEvent(_console, bufferIndex, blockId));
    }

    private void OnSubjectInjectPressed(int index, Button button)
    {
        _updateBuffer = true;
        button.Disabled = true;
        _entNetworkManager.SendSystemNetworkMessage(
            new DnaModifierConsoleSubjectInjectEvent(_console, index));
    }

    private void OnClearBufferPressed(int index)
    {
        _updateBuffer = true;
        _entNetworkManager.SendSystemNetworkMessage(
            new DnaModifierConsoleClearBufferEvent(_console, index));
    }

    private void OnRenameBufferPressed(int index)
    {
        _updateBuffer = true;
        var session = IoCManager.Resolve<IPlayerManager>().LocalSession;
        if (session?.AttachedEntity.HasValue == true)
        {
            var user = _entManager.GetNetEntity(session.AttachedEntity.Value);
            _entNetworkManager.SendSystemNetworkMessage(
                new DnaModifierConsoleRenameBufferEvent(_console, user, index));
        }
    }

    private void OnExportOnDiskPressed(int index)
    {
        _entNetworkManager.SendSystemNetworkMessage(
            new DnaModifierConsoleExportOnDiskEvent(_console, index));
    }

    private void OnExportFromDiskPressed(int index)
    {
        _entNetworkManager.SendSystemNetworkMessage(
            new DnaModifierConsoleExportFromDiskEvent(_console, index));
    }

    private sealed class ModifyButton : Button
    {
        public string Block { get; set; }
        public int Values { get; set; }

        public ModifyButton(string block, int values)
        {
            Block = block;
            Values = values;
        }
    }

    #region Initilize U.I.
    private void InitilizeUniqueIdentifiers(UniqueIdentifiersPrototype unique)
    {
        _initializedUi = true;
        var blocks = new List<(string BlockName, string[] Values)>
        {
            ("1", unique.HairColorR),
            ("2", unique.HairColorG),
            ("3", unique.HairColorB),
            ("4", unique.SecondaryHairColorR),
            ("5", unique.SecondaryHairColorG),
            ("6", unique.SecondaryHairColorB),
            ("7", unique.BeardColorR),
            ("8", unique.BeardColorG),
            ("9", unique.BeardColorB),
            ("13", unique.SkinTone),
            ("14", unique.FurColorR),
            ("15", unique.FurColorG),
            ("16", unique.FurColorB),
            ("17", unique.HeadAccessoryColorR),
            ("18", unique.HeadAccessoryColorG),
            ("19", unique.HeadAccessoryColorB),
            ("20", unique.HeadMarkingColorR),
            ("21", unique.HeadMarkingColorG),
            ("22", unique.HeadMarkingColorB),
            ("23", unique.BodyMarkingColorR),
            ("24", unique.BodyMarkingColorG),
            ("25", unique.BodyMarkingColorB),
            ("26", unique.TailMarkingColorR),
            ("27", unique.TailMarkingColorG),
            ("28", unique.TailMarkingColorB),
            ("29", unique.EyeColorR),
            ("30", unique.EyeColorG),
            ("31", unique.EyeColorB),
            ("32", unique.Gender),
            ("33", unique.BeardStyle),
            ("34", unique.HairStyle),
            ("35", unique.HeadAccessoryStyle),
            ("36", unique.HeadMarkingStyle),
            ("37", unique.BodyMarkingStyle),
            ("38", unique.TailMarkingStyle)
        };

        var rowContainer = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(0, 0, 0, 10)
        };

        var currentRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal
        };

        for (int i = 0; i < blocks.Count; i++)
        {
            CreateUiButtonsForBlock(blocks[i].BlockName, blocks[i].Values, currentRow);
            if ((i + 1) % 5 == 0 || i == blocks.Count - 1)
            {
                rowContainer.AddChild(currentRow);
                currentRow = new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Horizontal
                };
            }
        }

        UiContainer.AddChild(rowContainer);
    }

    private void CreateUiButtonsForBlock(string blockName, string[] values, BoxContainer parentContainer)
    {
        var blockContainer = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            Margin = new Thickness(5, 0, 5, 0)
        };

        var blockLabel = new Label
        {
            Text = blockName,
            MinWidth = 25,
            StyleClasses = { StyleNano.StyleClassLabelSecondaryColor }
        };

        blockContainer.AddChild(blockLabel);

        for (int i = 0; i < values.Length; i++)
        {
            var value = values[i];
            var button = new ModifyButton(blockName, i)
            {
                Text = value,
                MinWidth = 40,
                Margin = new Thickness(0, 2),
                ToggleMode = true
            };

            button.OnPressed += _ => OnButtonUiPressed(button);

            if (_activeButtonUi == null)
            {
                button.Pressed = true;
                _activeButtonUi = button;
            }

            blockContainer.AddChild(button);
        }

        parentContainer.AddChild(blockContainer);
    }

    private void OnButtonUiPressed(ModifyButton pressedButton)
    {
        if (pressedButton == _activeButtonUi)
        {
            pressedButton.Pressed = true;
            return;
        }

        if (_activeButtonUi != null)
        {
            _activeButtonUi.Pressed = false;
        }

        _activeButtonUi = pressedButton;
        pressedButton.Pressed = true;
    }
    #endregion

    #region Initilize S.E.
    private void InitilizeStructuralEnzymes(List<EnzymesPrototypeInfo> enzymes)
    {
        _initializedSe = true;

        var blocks = new List<(string BlockName, string[] Values)>();
        for (int i = 0; i < enzymes.Count; i++)
        {
            var enzyme = enzymes[i];
            var blockName = $"{i + 1}";
            var values = enzyme.HexCode;

            blocks.Add((blockName, values));
        }

        var rowContainer = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(0, 0, 0, 10)
        };

        var currentRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal
        };

        for (int i = 0; i < blocks.Count; i++)
        {
            CreateButtonsSeForBlock(blocks[i].BlockName, blocks[i].Values, currentRow);

            if ((i + 1) % 5 == 0 || i == blocks.Count - 1)
            {
                rowContainer.AddChild(currentRow);
                currentRow = new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Horizontal
                };
            }
        }

        SeContainer.AddChild(rowContainer);
    }

    private void CreateButtonsSeForBlock(string blockName, string[] values, BoxContainer parentContainer)
    {
        var blockContainer = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            Margin = new Thickness(5, 0, 5, 0)
        };

        var blockLabel = new Label
        {
            Text = blockName,
            MinWidth = 25,
            StyleClasses = { StyleNano.StyleClassLabelSecondaryColor }
        };

        blockContainer.AddChild(blockLabel);

        for (int i = 0; i < values.Length; i++)
        {
            var value = values[i];
            var button = new ModifyButton(blockName, i)
            {
                Text = value,
                MinWidth = 40,
                Margin = new Thickness(0, 2),
                ToggleMode = true
            };

            button.OnPressed += _ => OnButtonSePressed(button);

            if (_activeButtonSe == null)
            {
                button.Pressed = true;
                _activeButtonSe = button;
            }

            blockContainer.AddChild(button);
        }

        parentContainer.AddChild(blockContainer);
    }

    private void OnButtonSePressed(ModifyButton pressedButton)
    {
        if (pressedButton == _activeButtonSe)
        {
            pressedButton.Pressed = true;
            return;
        }

        if (_activeButtonSe != null)
        {
            _activeButtonSe.Pressed = false;
        }

        _activeButtonSe = pressedButton;
        pressedButton.Pressed = true;
    }
    #endregion

    #region Initilize Disk
    private void EnableBufferBlock(int bufferIndex)
    {
        switch (bufferIndex)
        {
            case 1:
                ClearButton1.Disabled = false;
                RenameButton1.Disabled = false;
                ExportButton1.Disabled = false;
                break;
            case 2:
                ClearButton2.Disabled = false;
                RenameButton2.Disabled = false;
                ExportButton2.Disabled = false;
                break;
            case 3:
                ClearButton3.Disabled = false;
                RenameButton3.Disabled = false;
                ExportButton3.Disabled = false;
                break;
            default: break;
        }
    }

    private void DisabledSubjectDiskBlock(int? bufferIndex)
    {
        if (bufferIndex != null)
        {
            switch (bufferIndex)
            {
                case 1:
                    SubjectUI1.Disabled = true;
                    SubjectUISE1.Disabled = true;
                    SubjectSE1.Disabled = true;
                    break;
                case 2:
                    SubjectUI2.Disabled = true;
                    SubjectUISE2.Disabled = true;
                    SubjectSE2.Disabled = true;
                    break;
                case 3:
                    SubjectUI3.Disabled = true;
                    SubjectUISE3.Disabled = true;
                    SubjectSE3.Disabled = true;
                    break;
                default: break;
            }
        }
        else
        {
            SubjectUI1.Disabled = true;
            SubjectUISE1.Disabled = true;
            SubjectSE1.Disabled = true;
            SubjectUI2.Disabled = true;
            SubjectUISE2.Disabled = true;
            SubjectSE2.Disabled = true;
            SubjectUI3.Disabled = true;
            SubjectUISE3.Disabled = true;
            SubjectSE3.Disabled = true;
        }
    }
    #endregion

    #region Initilize Rejuve
    private void CreateBeakerUI(Control control, ContainerInfo? info)
    {
        control.RemoveAllChildren();

        if (info == null)
        {
            control.AddChild(new Label
            {
                Text = Loc.GetString("dna-modifier-no-container-loaded-text")
            });
            return;
        }

        control.AddChild(new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            Children =
            {
                new Label { Text = $"{info.DisplayName}: " },
                new Label
                {
                    Text = $"{info.CurrentVolume}/{info.MaxVolume}",
                    StyleClasses = { StyleNano.StyleClassLabelSecondaryColor }
                }
            }
        });

        var rowCount = 0;
        if (info.Reagents != null)
        {
            foreach (var reagent in info.Reagents)
            {
                _prototypeManager.TryIndex(reagent.Reagent.Prototype, out ReagentPrototype? proto);
                var name = proto?.LocalizedName ?? Loc.GetString("dna-modifier-unknown-reagent-text");
                var reagentColor = proto?.SubstanceColor ?? default(Color);

                control.AddChild(BuildReagentRow(reagentColor, rowCount++, name, reagent.Reagent, reagent.Quantity, true));
            }
        }
    }

    private Control BuildReagentRow(Color reagentColor, int rowCount, string name, ReagentId reagent, FixedPoint2 quantity, bool addReagentButtons)
    {
        var rowColor1 = Color.FromHex("#1B1B1E");
        var rowColor2 = Color.FromHex("#202025");
        var currentRowColor = (rowCount % 2 == 1) ? rowColor1 : rowColor2;

        if (reagentColor == default(Color) || !addReagentButtons)
        {
            reagentColor = currentRowColor;
        }

        var reagentButtons = CreateReagentTransferButtons(reagent, addReagentButtons);
        var rowContainer = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            Children =
            {
                new Label { Text = $"{name}: " },
                new Label
                {
                    Text = $"{quantity}u",
                    StyleClasses = { StyleNano.StyleClassLabelSecondaryColor }
                },
                new Control { HorizontalExpand = true },
                new PanelContainer
                {
                    Name = "colorPanel",
                    VerticalExpand = true,
                    MinWidth = 4,
                    PanelOverride = new StyleBoxFlat
                    {
                        BackgroundColor = reagentColor
                    },
                    Margin = new Thickness(0, 1)
                }
            }
        };

        foreach (var reagentButton in reagentButtons)
        {
            rowContainer.AddChild(reagentButton);
        }

        return new PanelContainer
        {
            PanelOverride = new StyleBoxFlat(currentRowColor),
            Children = { rowContainer }
        };
    }

    private List<ReagentButton> CreateReagentTransferButtons(ReagentId reagent, bool addReagentButtons)
    {
        if (!addReagentButtons) return new List<ReagentButton>();

        var buttonConfigs = new (string text, DnaModifierReagentAmount amount, string styleClass)[]
        {
            ("1", DnaModifierReagentAmount.U1, StyleBase.ButtonOpenBoth),
            ("5", DnaModifierReagentAmount.U5, StyleBase.ButtonOpenBoth),
            ("10", DnaModifierReagentAmount.U10, StyleBase.ButtonOpenBoth),
            ("25", DnaModifierReagentAmount.U25, StyleBase.ButtonOpenBoth),
            ("50", DnaModifierReagentAmount.U50, StyleBase.ButtonOpenBoth),
            ("100", DnaModifierReagentAmount.U100, StyleBase.ButtonOpenBoth),
            ("All", DnaModifierReagentAmount.All, StyleBase.ButtonOpenLeft),
        };

        var buttons = new List<ReagentButton>();
        foreach (var (text, amount, styleClass) in buttonConfigs)
        {
            var reagentTransferButton = new ReagentButton(text, amount, reagent, styleClass);
            reagentTransferButton.OnPressed += args =>
            {
                _entNetworkManager.SendSystemNetworkMessage(
                    new DnaModifierConsoleReagentButtonEvent(_console, amount, reagent));
            };
            buttons.Add(reagentTransferButton);
        }

        return buttons;
    }

    private sealed class ReagentButton : Button
    {
        public DnaModifierReagentAmount Amount { get; set; }
        public ReagentId Id { get; set; }

        public ReagentButton(string text, DnaModifierReagentAmount amount, ReagentId id, string styleClass)
        {
            AddStyleClass(styleClass);
            Text = text;
            Amount = amount;
            Id = id;
        }
    }
    #endregion
}
