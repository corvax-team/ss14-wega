using System.Linq;
using Content.Shared.DoAfter;
using Content.Shared.Surgery;
using Content.Shared.Surgery.Components;
using Content.Shared.Tag;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server.Surgery;

public sealed partial class SurgerySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfterSystem = default!;

    private void GraphsInitialize()
    {
        SubscribeLocalEvent<OperatedComponent, SurgeryStartMessage>(OnSurgeryStart);
        SubscribeLocalEvent<OperatedComponent, SurgeryStepDoAfterEvent>(OnSurgeryStepDoAfter);
    }

    /// <summary>
    /// Handles the start of a surgery procedure, setting up the operation state and starting the operation chain.
    /// </summary>
    /// <param name="uid">The entity UID of the patient being operated on.</param>
    /// <param name="comp">The OperatedComponent associated with the patient.</param>
    /// <param name="args">The SurgeryStartMessage containing details about the surgery start request.</param>
    private void OnSurgeryStart(EntityUid uid, OperatedComponent comp, SurgeryStartMessage args)
    {
        var user = GetEntity(args.User);
        if (comp.GraphId == null)
            return;

        if (!comp.IsOperating)
        {
            comp.SetOperationState(args.TargetNode, user);
            StartOperationChain(user, uid, comp, args.TargetNode, args.StepIndex, args.IsParallel);
        }
        else
        {
            if (args.TargetNode != comp.CurrentTargetNode && comp.CurrentStepIndex == 0)
                comp.SetOperationState(args.TargetNode, user);

            StartOperationChain(user, uid, comp, comp.CurrentTargetNode!, args.StepIndex, args.IsParallel);
        }
    }

    /// <summary>
    /// Handles the completion of a surgery step, updating the operation state and checking for transition progress.
    /// </summary>
    /// <param name="uid">The entity UID of the patient being operated on.</param>
    /// <param name="comp">The OperatedComponent associated with the patient.</param>
    /// <param name="args">The SurgeryStepDoAfterEvent containing details about the completed step.</param>
    private void OnSurgeryStepDoAfter(EntityUid uid, OperatedComponent comp, SurgeryStepDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || comp.GraphId == null)
            return;

        var graph = _proto.Index(comp.GraphId.Value);
        var currentNodeProto = _proto.Index(comp.CurrentNode);
        if (currentNodeProto == null)
            return;

        var transition = GetTransitionForNode(currentNodeProto, args.TargetNode);
        if (transition == null)
            return;

        var group = transition.StepGroups.FirstOrDefault(g => g.Parallel == args.IsParallel);
        if (group == null)
            return;

        SurgeryStep? step = null;
        if (args.IsParallel && args.StepIndex is int stepIdx)
        {
            step = group.Steps.ElementAtOrDefault(stepIdx);
            if (step != null)
            {
                comp.CompletedParallelSteps.Add(step);
            }
        }
        else
        {
            if (!comp.CompletedSteps.ContainsKey(args.TargetNode))
                comp.CompletedSteps[args.TargetNode] = new HashSet<SurgeryStep>();

            step = group.Steps.ElementAtOrDefault(comp.CurrentStepIndex);
            if (step != null)
            {
                comp.CompletedSteps[args.TargetNode].Add(step);
                comp.CurrentStepIndex++;
            }
        }

        EntityUid? item = null;
        if (args.Used != null)
            item = args.Used.Value;

        if (step != null)
        {
            if (step.Sound != null)
            {
                _audio.PlayPredicted(step.Sound, uid, null);
            }

            bool foundMatch = false;
            float successModifier = 1f;
            if (step.Tool != null && step.Tool.Count > 0 && item != null)
            {
                if (_tool.HasQuality(item.Value, step.Tool[0]))
                {
                    successModifier = 1f;
                    foundMatch = true;
                }
                else if (step.Tool.Count > 1)
                {
                    for (int i = 1; i < step.Tool.Count; i++)
                    {
                        if (_tool.HasQuality(item.Value, step.Tool[i]))
                        {
                            successModifier = 1f - i * 0.1f;
                            foundMatch = true;
                            break;
                        }
                    }
                }
            }

            if (!foundMatch && step.Tag != null && step.Tag.Count > 0 && item != null)
            {
                for (int i = 0; i < step.Tag.Count; i++)
                {
                    if (_tag.HasTag(item.Value, step.Tag[i]))
                    {
                        successModifier = 0.9f - i * 0.1f;
                        foundMatch = true;
                        break;
                    }
                }
            }

            float finalSuccessChance = step.SuccessChance * successModifier;
            PerformSurgeryEffect(step.Action, step.RequiredPart, step.DamageType, finalSuccessChance, step.FailureEffect, uid, item);
        }

        CheckTransitionProgress(uid, comp, graph, transition);
        UpdateUi(uid, comp, graph);
    }

    #region Handle Steps

    /// <summary>
    /// Adds steps from a surgery node to the list of groups for UI display.
    /// </summary>
    /// <param name="node">The surgery node containing the transitions and steps.</param>
    /// <param name="patient">The entity UID of the patient.</param>
    /// <param name="comp">The OperatedComponent associated with the patient.</param>
    /// <param name="groups">The list of SurgeryGroupDto to populate with step data.</param>
    private void AddNodeSteps(SurgeryNode node, EntityUid patient, OperatedComponent comp, List<SurgeryGroupDto> groups)
    {
        foreach (var transition in node.Transitions)
        {
            var steps = new List<SurgeryStepDto>();
            FlattenStepsFromGroups(transition.StepGroups, steps, patient, comp, transition.Target);

            if (steps.Any(s => s.IsEnabled && s.IsVisible))
            {
                groups.Add(new SurgeryGroupDto(
                    groupName: Loc.GetString(transition.Label),
                    description: $"Процедура: {transition.Label}",
                    targetNode: transition.Target,
                    steps: steps
                ));
            }
        }
    }

    /// <summary>
    /// Flattens steps from step groups into a list of SurgeryStepDto for UI display.
    /// </summary>
    /// <param name="groups">The list of SurgeryStepGroup to process.</param>
    /// <param name="output">The output list of SurgeryStepDto to populate.</param>
    /// <param name="patient">The entity UID of the patient.</param>
    /// <param name="comp">The OperatedComponent associated with the patient.</param>
    /// <param name="target">The target node ID for the steps.</param>
    private void FlattenStepsFromGroups(List<SurgeryStepGroup> groups, List<SurgeryStepDto> output,
        EntityUid patient, OperatedComponent comp, string target)
    {
        foreach (var group in groups)
        {
            if (!CheckGroupConditions(patient, group))
                continue;

            if (group.Parallel)
            {
                output.AddRange(group.Steps.Select(s => new SurgeryStepDto(
                    name: $"(Параллельно) {s.Action}",
                    isCompleted: comp.CompletedParallelSteps.Contains(s),
                    isEnabled: !comp.CompletedParallelSteps.Contains(s) && CheckStepConditions(patient, s),
                    isVisible: CheckStepConditions(patient, s),
                    requiredTool: s.Tool?.FirstOrDefault().ToString(),
                    requiredCondition: s.RequiredPart
                )));
            }
            else
            {
                foreach (var s in group.Steps)
                {
                    var isCompleted = IsStepCompleted(comp, target, s);
                    output.Add(new SurgeryStepDto(
                        name: s.Action.ToString(),
                        isCompleted: isCompleted,
                        isEnabled: !isCompleted,
                        isVisible: CheckStepConditions(patient, s),
                        requiredTool: s.Tool?.FirstOrDefault().ToString(),
                        requiredCondition: s.RequiredPart
                    ));
                }
            }
        }
    }

    /// <summary>
    /// Converts a SurgeryNodePrototype into a runtime SurgeryNode.
    /// </summary>
    /// <param name="proto">The SurgeryNodePrototype to convert.</param>
    /// <returns>The converted SurgeryNode.</returns>
    private SurgeryNode ConvertToRuntimeNode(SurgeryNodePrototype proto)
    {
        var transitions = new List<SurgeryTransition>();
        foreach (var transitionId in proto.TransitionIds)
        {
            if (_proto.TryIndex(transitionId, out SurgeryTransitionPrototype? transitionProto))
            {
                transitions.Add(new SurgeryTransition
                {
                    Target = transitionProto.Target,
                    Label = transitionProto.Label,
                    StepGroups = transitionProto.StepGroups.ToList()
                });
            }
        }

        return new SurgeryNode
        {
            Name = proto.ID,
            Description = proto.Description,
            BodyPart = proto.BodyPart,
            Transitions = transitions
        };
    }

    /// <summary>
    /// Checks if a surgery step has been completed for a specific target node.
    /// </summary>
    private bool IsStepCompleted(OperatedComponent comp, string target, SurgeryStep step)
        => comp.CompletedSteps.TryGetValue(target, out var completed) && completed.Contains(step);

    #endregion

    #region Operation Processing

    /// <summary>
    /// Starts a chain of surgery operations for a specific target node.
    /// </summary>
    /// <param name="user">The entity UID of the user performing the surgery.</param>
    /// <param name="patient">The entity UID of the patient.</param>
    /// <param name="comp">The OperatedComponent associated with the patient.</param>
    /// <param name="targetNode">The target node ID for the surgery.</param>
    /// <param name="stepIndex">The index of the step to start (optional).</param>
    /// <param name="isParallel">Whether the steps should be performed in parallel.</param>
    private void StartOperationChain(EntityUid user, EntityUid patient, OperatedComponent comp, ProtoId<SurgeryNodePrototype>? targetNode, int stepIndex = -1, bool isParallel = false)
    {
        if (comp.GraphId == null || targetNode == null)
            return;

        var graph = _proto.Index(comp.GraphId.Value);
        SurgeryNodePrototype? currentNodeProto = comp.CurrentNode == "Default"
            ? graph.GetStartNodes().FirstOrDefault(n => HasTransitionToTarget(n, targetNode.Value))
            : _proto.Index(comp.CurrentNode);

        if (currentNodeProto == null)
            return;

        comp.CurrentNode = currentNodeProto.ID;
        var transition = GetTransitionForNode(currentNodeProto, targetNode.Value);
        if (transition == null)
            return;

        var group = transition.StepGroups.FirstOrDefault(g => g.Parallel == isParallel);
        if (group == null)
            return;

        if (isParallel)
        {
            if (stepIndex >= 0 && stepIndex < group.Steps.Count)
            {
                StartSingleStep(user, patient, comp, transition.Target, group.Steps[stepIndex], true, transition);
            }
            else
            {
                foreach (var step in group.Steps)
                {
                    StartSingleStep(user, patient, comp, transition.Target, step, true, transition);
                }
            }
        }
        else
        {
            var step = group.Steps.ElementAtOrDefault(comp.CurrentStepIndex);
            if (step != null)
            {
                StartSingleStep(user, patient, comp, transition.Target, step, false, transition);
            }
        }
    }

    /// <summary>
    /// Starts a single surgery step, performing validation and initiating the DoAfter process.
    /// </summary>
    /// <param name="user">The entity UID of the user performing the surgery.</param>
    /// <param name="patient">The entity UID of the patient.</param>
    /// <param name="comp">The OperatedComponent associated with the patient.</param>
    /// <param name="targetNode">The target node ID for the surgery.</param>
    /// <param name="step">The SurgeryStep to perform.</param>
    /// <param name="isParallel">Whether the step is part of a parallel group.</param>
    /// <param name="transition">The SurgeryTransition associated with the step.</param>
    private void StartSingleStep(EntityUid user, EntityUid patient, OperatedComponent comp, string targetNode, SurgeryStep step, bool isParallel, SurgeryTransition transition)
    {
        if (!TryGetOperatingTable(patient, out var tableModifier) && !comp.OperatedPart)
            return;

        var skillMod = TryComp<SurgicalSkillComponent>(user, out var skill) ? skill.Modifier : 1f;
        var time = step.Time * skillMod / tableModifier;
        if (user == patient)
            time *= HasComp<SurgicalSkillComponent>(user) ? 3f : 5f;

        var item = _hands.GetActiveItemOrSelf(user);

        bool toolValid = step.Tool == null || step.Tool.Count == 0 || step.Tool.Any(tool => _tool.HasQuality(item, tool));
        bool tagValid = step.Tag == null || step.Tag.Count == 0 || step.Tag.Any(tag => _tag.HasTag(item, tag));

        if (!toolValid && !tagValid)
        {
            _popup.PopupEntity(Loc.GetString("surgery-missing-tool"), user, user);
            return;
        }

        int? stepIndex = null;

        if (isParallel)
        {
            var parallelGroup = transition.StepGroups.FirstOrDefault(g => g.Parallel && g.Steps.Contains(step));
            if (parallelGroup != null)
                stepIndex = parallelGroup.Steps.IndexOf(step);
        }

        var args = new DoAfterArgs(EntityManager, user, time,
            new SurgeryStepDoAfterEvent(targetNode, isParallel, stepIndex), patient, used: item)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true
        };

        _doAfterSystem.TryStartDoAfter(args);
    }

    /// <summary>
    /// Checks the progress of a surgery transition to determine if all steps have been completed.
    /// </summary>
    /// <param name="uid">The entity UID of the patient.</param>
    /// <param name="comp">The OperatedComponent associated with the patient.</param>
    /// <param name="graph">The SurgeryGraphPrototype containing the transition data.</param>
    /// <param name="transition">The SurgeryTransition to check.</param>
    private void CheckTransitionProgress(EntityUid uid, OperatedComponent comp, SurgeryGraphPrototype graph, SurgeryTransition transition)
    {
        var steps = transition.StepGroups.SelectMany(g => g.Steps);
        var allDone = steps.All(s =>
            comp.CompletedSteps.TryGetValue(transition.Target, out var set) && set.Contains(s));

        if (allDone)
        {
            comp.ResetOperationState(transition.Target);
            comp.CurrentNode = transition.Target;
            Dirty(uid, comp);
            UpdateUi(uid, comp, graph);
        }
    }

    #endregion

    #region Validation

    /// <summary>
    /// Retrieves the transition for a node that leads to a specific target node.
    /// </summary>
    /// <param name="node">The SurgeryNodePrototype to check for transitions.</param>
    /// <param name="targetNode">The target node ID to find.</param>
    /// <returns>The matching SurgeryTransition, or null if not found.</returns>
    private SurgeryTransition? GetTransitionForNode(SurgeryNodePrototype node, ProtoId<SurgeryNodePrototype> targetNode)
    {
        foreach (var transitionId in node.TransitionIds)
        {
            if (_proto.TryIndex(transitionId, out SurgeryTransitionPrototype? transitionProto) &&
                transitionProto.Target == targetNode)
            {
                return new SurgeryTransition
                {
                    Target = transitionProto.Target,
                    Label = transitionProto.Label,
                    StepGroups = transitionProto.StepGroups
                };
            }
        }
        return null;
    }

    /// <summary>
    /// Checks if a node has a transition to a specific target node.
    /// </summary>
    /// <param name="node">The SurgeryNodePrototype to check.</param>
    /// <param name="target">The target node ID to find.</param>
    /// <returns>True if a transition exists, otherwise false.</returns>
    private bool HasTransitionToTarget(SurgeryNodePrototype node, ProtoId<SurgeryNodePrototype> target)
    {
        foreach (var transitionId in node.TransitionIds)
        {
            if (_proto.TryIndex(transitionId, out SurgeryTransitionPrototype? transition) &&
                transition.Target == target)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Checks if all conditions for a surgery step group are satisfied.
    /// </summary>
    /// <param name="patient">The entity UID of the patient.</param>
    /// <param name="group">The SurgeryStepGroup to check.</param>
    /// <returns>True if all conditions are satisfied, otherwise false.</returns>
    private bool CheckGroupConditions(EntityUid patient, SurgeryStepGroup group)
    {
        if (group.Conditions == null || group.Conditions.Count == 0)
            return true;

        foreach (var condition in group.Conditions)
        {
            if (!condition.CheckWithInvert(patient, EntityManager))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Checks if all conditions for a surgery step are satisfied.
    /// </summary>
    /// <param name="patient">The entity UID of the patient.</param>
    /// <param name="step">The SurgeryStep to check.</param>
    /// <returns>True if all conditions are satisfied, otherwise false.</returns>
    private bool CheckStepConditions(EntityUid patient, SurgeryStep step)
    {
        if (step.Conditions == null || step.Conditions.Count == 0)
            return true;

        if (!CheckConditions(patient, step))
            return false;

        return true;
    }

    /// <summary>
    /// Checks all conditions for a surgery step, including inverted conditions.
    /// </summary>
    /// <param name="patient">The entity UID of the patient.</param>
    /// <param name="step">The SurgeryStep to check.</param>
    /// <returns>True if all conditions are satisfied, otherwise false.</returns>
    private bool CheckConditions(EntityUid patient, SurgeryStep step)
    {
        foreach (var condition in step.Conditions)
        {
            if (!condition.CheckWithInvert(patient, EntityManager))
                return false;
        }
        return true;
    }

    #endregion
}
