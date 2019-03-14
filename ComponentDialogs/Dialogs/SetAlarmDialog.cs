using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ComponentDialogs.Dialogs
{
    public class SetAlarmDialog : ComponentDialog
    {
        private const string InitialId = "mainDialog";
        private const string AlarmPrompt = "dateTimePrompt";

        private  ComponentDialogsAccessors _acessors;

        /// <summary>
        /// Making state accessors available in component dialog 
        /// </summary>
        /// <param name="id"></param>
        /// <param name="accessors"></param>
        public SetAlarmDialog(string id, ComponentDialogsAccessors accessors) : base(id)
        {
            InitialDialogId = InitialId;

            // Define the prompts used in this conversation flow.
            // Ideally, we'd add validation to this prompt.
            AddDialog(new DateTimePrompt(AlarmPrompt));

            _acessors = accessors;

            // Define the conversation flow using a waterfall model.
            WaterfallStep[] waterfallSteps = new WaterfallStep[]
            {
                AlarmStepAsync,
                FinalStepAsync,
            };

            AddDialog(new WaterfallDialog(InitialId, waterfallSteps));
        }

        private  async Task<DialogTurnResult> AlarmStepAsync(
            WaterfallStepContext step,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            string greeting = step.Options is GuestInfo guest
                    && !string.IsNullOrWhiteSpace(guest?.Name)
                    ? $"Hi {guest.Name}" : "Hi";


            //Testing step values in non static approach
            step.Values["Test"] = "test";

            //Beaware if you want to use accessors these methods cannot be static as in default implementation
            await _acessors.AlarmSpecificDialogState.SetAsync(step.Context, "***hello***");

            string prompt = $"{greeting}. When would you like your alarm set for?";
            return await step.PromptAsync(
                AlarmPrompt,
                new PromptOptions { Prompt = MessageFactory.Text(prompt) },
                cancellationToken);
        }

        private async Task<DialogTurnResult> FinalStepAsync(
            WaterfallStepContext step,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            // Ambiguous responses can generate multiple results.
            var resolution = (step.Result as IList<DateTimeResolution>)?.FirstOrDefault();

            // Time ranges have a start and no value.
            var alarm = resolution.Value ?? resolution.Start;
            string roomNumber = (step.Options as UserInfo)?.Guest.Room;

            var value = await _acessors.AlarmSpecificDialogState.GetAsync(step.Context);

            // Send a confirmation message.
            await step.Context.SendActivityAsync(
                $"{value} {step.Values["test"]} Your alarm is set to {alarm} for room {roomNumber}.",
                cancellationToken: cancellationToken);

            // End the dialog, returning the alarm info.
            return await step.EndDialogAsync(
                new WakeUpInfo { Time = alarm },
                cancellationToken);
        }
    }
}
