namespace HelpDeskBot.Dialogs
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using HelpDeskBot.Services;
    using HelpDeskBot.Util;
    using Microsoft.Bot.Builder.Scorables.Internals;
    using Microsoft.Bot.Connector;

    public class SearchScorable : ScorableBase<IActivity, string, double>
    {
        //private const string TRIGGER = "search about ";
        private const string TRIGGER = "を検索";
        private readonly AzureSearchService searchService = new AzureSearchService();

        protected override Task DoneAsync(IActivity item, string state, CancellationToken token)
        {
            return Task.CompletedTask;
        }

        protected override double GetScore(IActivity item, string state)
        {
            return 1.0;
        }

        protected override bool HasScore(IActivity item, string state)
        {
            return !string.IsNullOrWhiteSpace(state);
        }

        protected async override Task PostAsync(IActivity item, string state, CancellationToken token)
        {
            var searchResult = await this.searchService.Search(state);

            var replyActivity = ((Activity)item).CreateReply();
            await CardUtil.ShowSearchResults(replyActivity, searchResult, $"申し訳ありません。「{state}」を理解できませんでした。\n'ヘルプ' または 'help' と入力すると、ヘルプメニューを表示します。");
        }

        protected async override Task<string> PrepareAsync(IActivity item, CancellationToken token)
        {
            var message = item.AsMessageActivity();
            if (message != null && !string.IsNullOrWhiteSpace(message.Text))
            {
                if (message.Text.Trim().EndsWith(TRIGGER, StringComparison.InvariantCultureIgnoreCase))
                {
                    //return message.Text.Substring(TRIGGER.Length);
                    return message.Text.Substring(0, (message.Text.Length - TRIGGER.Length));
                }
            }

            return null;
        }
    }
}