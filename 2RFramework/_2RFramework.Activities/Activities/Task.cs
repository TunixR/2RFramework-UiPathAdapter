using _2RFramework.Activities.Properties;
using _2RFramework.Activities.Utilities;
using _2RFramework.Models;
using System;
using System.Activities;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using UiPath.Shared.Activities.Localization;
using Activity = System.Activities.Activity;

namespace _2RFramework.Activities;

/// <summary>
///     Represents a custom activity that executes a collection of child activities in sequence.
/// </summary>
[LocalizedDisplayName(nameof(Resources.Task_DisplayName))]
[LocalizedDescription(nameof(Resources.Task_Description))]
public class Task : NativeActivity
{
    #region Properties

    /// <summary>
    ///     Gets or sets the name of the task.
    /// </summary>
    [Browsable(true)]
    [LocalizedCategory(nameof(Resources.Common_Category))]
    [DisplayName("Task Name")]
    [Description("Name of the task")]
    public InArgument<string> TaskName { get; set; }

    /// <summary>
    ///     Collection of activities that will be executed by this task.
    ///     This allows multiple activities without using a Sequence container.
    /// </summary>
    [Browsable(true)]
    [LocalizedCategory(nameof(Resources.Common_Category))]
    [DisplayName("Activities")]
    [Description("Collection of activities to be executed by this task")]
    public List<Activity> Activities { get; set; } = new();

    // Object Container: Add strongly-typed objects here and they will be available in the scope's child activities.
    private int _currentActivityIndex;

    #endregion

    #region Protected Methods

    /// <inheritdoc />
    protected override void Execute(NativeActivityContext context)
    {
        // Load environment variables from .env file
        EnvReader.Load(".env");

        // If there are no activities, just return
        if (Activities == null || Activities.Count == 0) return;

        // Reset the index and start execution
        _currentActivityIndex = 0;
        ScheduleNext(context);
    }

    #endregion


    #region Helpers

    private void ScheduleNext(NativeActivityContext context)
    {
        if (_currentActivityIndex < Activities.Count)
            // Schedule the activity with handlers for completion and faulting
            context.ScheduleActivity(Activities[_currentActivityIndex], OnCompleted, OnFaulted);
    }

    #endregion


    #region Events

    /// <summary>
    ///     In this method, we recollect task and process data to invoke 2RAgent via API, which must then try and resolve the
    ///     error.
    ///     If the error is resolved, we can continue to the next activity.
    /// </summary>
    /// <param name="faultContext"></param>
    /// <param name="propagatedException"></param>
    /// <param name="propagatedFrom"></param>
    private void OnFaulted(NativeActivityFaultContext faultContext, Exception propagatedException,
        ActivityInstance propagatedFrom)
    {
        var taskNameValue = TaskName.Get(faultContext);

        // Get workflow variables using the utility method
        var workflowVariables = TaskUtils.GetWorkflowVariables(this);

        // We want the following properties of activities:
        // - Index
        // - Type (e.g., Write Line, If, etc.)
        // - Attributes (e.g., MessageBox text, If condition, etc.)
        var previousActivities = Activities.Take(_currentActivityIndex)
            .Select(a => TaskUtils.GetActivityInfo(a, workflowVariables))
            .ToList();
        var failedActivity = TaskUtils.GetActivityInfo(Activities[_currentActivityIndex], workflowVariables);
        var reversedAct = Activities;
        reversedAct.Reverse();
        var futureActivities = reversedAct.Take(_currentActivityIndex)
            .Select(a => TaskUtils.GetActivityInfo(a, workflowVariables))
            .ToList();

        var message = new
        {
            code = propagatedException.Message,
            variables = workflowVariables,
            details = new
            {
                taskName = taskNameValue,
                previousActivities,
                failedActivity,
                futureActivities,
            }
        };

        string apiEndpoint = Environment.GetEnvironmentVariable("API_ENDPOINT");
        var response = TaskUtils.CallRecoveryAPI(message, apiEndpoint, null);

        // TODO: parse activity to continue from and changes to robot

        // Mark the exception as handled
        faultContext.HandleFault();

        // Move to the next activity
        _currentActivityIndex++;
        ScheduleNext(faultContext);
    }

    private void OnCompleted(NativeActivityContext context, ActivityInstance completedInstance)
    {
        // Move to the next activity and schedule it
        _currentActivityIndex++;
        ScheduleNext(context);
    }

    #endregion
}