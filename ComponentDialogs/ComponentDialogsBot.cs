// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ComponentDialogs.Dialogs;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Logging;

namespace ComponentDialogs
{
    /// <summary>
    /// Represents a bot that processes incoming activities.
    /// For each user interaction, an instance of this class is created and the OnTurnAsync method is called.
    /// This is a Transient lifetime service.  Transient lifetime services are created
    /// each time they're requested. For each Activity received, a new instance of this
    /// class is created. Objects that are expensive to construct, or have a lifetime
    /// beyond the single turn, should be carefully managed.
    /// For example, the <see cref="MemoryStorage"/> object and associated
    /// <see cref="IStatePropertyAccessor{T}"/> object are created with a singleton lifetime.
    /// </summary>
    /// <seealso cref="https://docs.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection?view=aspnetcore-2.1"/>
    public class ComponentDialogsBot : IBot
    {

        private const string MainDialogId = "mainDialog";
        private const string CheckInDialogId = "checkInDialog";
        private const string AlarmDialogId = "alarmDialog";

        private readonly ComponentDialogsAccessors _accessors;
        private readonly ILogger _logger;
        private readonly DialogSet _dialogs;

        /// <summary>
        /// Initializes a new instance of the class.
        /// </summary>
        /// <param name="conversationState">The managed conversation state.</param>
        /// <param name="loggerFactory">A <see cref="ILoggerFactory"/> that is hooked to the Azure App Service provider.</param>
        /// <seealso cref="https://docs.microsoft.com/en-us/aspnet/core/fundamentals/logging/?view=aspnetcore-2.1#windows-eventlog-provider"/>
        public ComponentDialogsBot(ConversationState conversationState, UserState userState, ILoggerFactory loggerFactory)
        {
            if (conversationState == null)
            {
                throw new System.ArgumentNullException(nameof(conversationState));
            }

            if (loggerFactory == null)
            {
                throw new System.ArgumentNullException(nameof(loggerFactory));
            }

            _accessors = new ComponentDialogsAccessors(conversationState, userState)
            {
                DialogState = conversationState.CreateProperty<DialogState>(ComponentDialogsAccessors.DialogStateAccessorName),
                UserInfoState = userState.CreateProperty<UserInfo>(ComponentDialogsAccessors.UserInfoAccessorName),
                AlarmSpecificDialogState = conversationState.CreateProperty<String>(ComponentDialogsAccessors.AlarmSpecificDialogStateAccessorName)
                

            };

            _logger = loggerFactory.CreateLogger<ComponentDialogsBot>();
            _logger.LogTrace("Turn start.");

            // Define the steps of the main dialog.
            WaterfallStep[] steps = new WaterfallStep[]
            {
                MenuStepAsync,
                HandleChoiceAsync,
                LoopBackAsync,
            };

            // Create our bot's dialog set, adding a main dialog and the three component dialogs.
            _dialogs = new DialogSet(_accessors.DialogState)
                .Add(new WaterfallDialog(MainDialogId, steps))
                .Add(new CheckInDialog(CheckInDialogId))
                .Add(new SetAlarmDialog(AlarmDialogId, _accessors));
                
        }

        /// <summary>
        /// Every conversation turn for our Echo Bot will call this method.
        /// There are no dialogs used, since it's "single turn" processing, meaning a single
        /// request and response.
        /// </summary>
        /// <param name="turnContext">A <see cref="ITurnContext"/> containing all the data needed
        /// for processing this conversation turn. </param>
        /// <param name="cancellationToken">(Optional) A <see cref="CancellationToken"/> that can be used by other objects
        /// or threads to receive notice of cancellation.</param>
        /// <returns>A <see cref="Task"/> that represents the work queued to execute.</returns>
        /// <seealso cref="BotStateSet"/>
        /// <seealso cref="ConversationState"/>
        /// <seealso cref="IMiddleware"/>
        public async Task OnTurnAsync(ITurnContext turnContext, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (turnContext.Activity.Type == ActivityTypes.Message)
            {
                // Establish dialog state from the conversation state.
                DialogContext dc = await _dialogs.CreateContextAsync(turnContext, cancellationToken);

                // Get the user's info.
                UserInfo userInfo = await _accessors.UserInfoState.GetAsync(turnContext, () => new UserInfo(), cancellationToken);

              
                // Continue any current dialog.
                DialogTurnResult dialogTurnResult = await dc.ContinueDialogAsync();

                // Process the result of any complete dialog.
                if (dialogTurnResult.Status is DialogTurnStatus.Complete)
                {
                    switch (dialogTurnResult.Result)
                    {
                        case GuestInfo guestInfo:
                            // Store the results of the check-in dialog.
                            userInfo.Guest = guestInfo;
                            await _accessors.UserInfoState.SetAsync(turnContext, userInfo, cancellationToken);
                            break;
                        default:
                            // We shouldn't get here, since the main dialog is designed to loop.
                            break;
                    }
                }

                // Every dialog step sends a response, so if no response was sent,
                // then no dialog is currently active.
                else if (!turnContext.Responded)
                {
                    if (string.IsNullOrEmpty(userInfo.Guest?.Name))
                    {
                        // If we don't yet have the guest's info, start the check-in dialog.
                        await dc.BeginDialogAsync(CheckInDialogId, null, cancellationToken);
                    }
                    else
                    {
                        // Otherwise, start our bot's main dialog.
                        await dc.BeginDialogAsync(MainDialogId, null, cancellationToken);
                    }
                }

                // Save the new turn count into the conversation state.
                await _accessors.ConversationState.SaveChangesAsync(turnContext, false, cancellationToken);
                await _accessors.UserState.SaveChangesAsync(turnContext, false, cancellationToken);
            }
            else
            {
                await turnContext.SendActivityAsync($"{turnContext.Activity.Type} event detected");
            }
        }

        private static async Task<DialogTurnResult> MenuStepAsync(
    WaterfallStepContext stepContext,
    CancellationToken cancellationToken = default(CancellationToken))
        {

            
            // Present the user with a set of "suggested actions".
            List<string> menu = new List<string> { "Reserve Table", "Wake Up" };
            await stepContext.Context.SendActivityAsync(
                MessageFactory.SuggestedActions(menu, "How can I help you?"),
                cancellationToken: cancellationToken);
            return Dialog.EndOfTurn;
        }

        private async Task<DialogTurnResult> HandleChoiceAsync(
            WaterfallStepContext stepContext,
            CancellationToken cancellationToken = default(CancellationToken))
        {

            // Get the user's info. (Since the type factory is null, this will throw if state does not yet have a value for user info.)
            UserInfo userInfo = await _accessors.UserInfoState.GetAsync(stepContext.Context, null, cancellationToken);

            // Check the user's input and decide which dialog to start.
            // Pass in the guest info when starting either of the child dialogs.
            string choice = (stepContext.Result as string)?.Trim()?.ToLowerInvariant();
            switch (choice)
            {
                //case "reserve table":
                //    return await stepContext.BeginDialogAsync(TableDialogId, userInfo.Guest, cancellationToken);

                case "wake up":
                    return await stepContext.BeginDialogAsync(AlarmDialogId, userInfo, cancellationToken);

                default:
                    // If we don't recognize the user's intent, start again from the beginning.
                    await stepContext.Context.SendActivityAsync(
                        "Sorry, I don't understand that command. Please choose an option from the list.");
                    return await stepContext.ReplaceDialogAsync(MainDialogId, null, cancellationToken);
            }
        }

        private async Task<DialogTurnResult> LoopBackAsync(
            WaterfallStepContext stepContext,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            // Get the user's info. (Because the type factory is null, this will throw if state does not yet have a value for user info.)
            UserInfo userInfo = await _accessors.UserInfoState.GetAsync(stepContext.Context, null, cancellationToken);

            // Process the return value from the child dialog.
            switch (stepContext.Result)
            {
                case TableInfo table:
                    // Store the results of the reserve-table dialog.
                    userInfo.Table = table;
                    await _accessors.UserInfoState.SetAsync(stepContext.Context, userInfo, cancellationToken);
                    break;
                case WakeUpInfo alarm:
                    // Store the results of the set-wake-up-call dialog.
                    userInfo.WakeUp = alarm;
                    await _accessors.UserInfoState.SetAsync(stepContext.Context, userInfo, cancellationToken);
                    break;
                default:
                    // We shouldn't get here, since these are no other branches that get this far.
                    break;
            }

            // Restart the main menu dialog.
            return await stepContext.ReplaceDialogAsync(MainDialogId, null, cancellationToken);
        }
    }
}
