using System;
using System.Threading.Tasks;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Connector;
using HelpDeskBot.Util;
using System.Collections.Generic;
using AdaptiveCards;
using Microsoft.Bot.Builder.Luis;
using Microsoft.Bot.Builder.Luis.Models;
using HelpDeskBot.Services;
using HelpDeskBot.Model;

namespace HelpDeskBot.Dialogs
{
    [LuisModel("LUISAppID", "LUISSubKey")]
    [Serializable]
    public class RootDialog : LuisDialog<object>
    {
        private string category;
        private string severity;
        private string description;

        [LuisIntent("")]
        [LuisIntent("None")]
        public async Task None(IDialogContext context, LuisResult result)
        {
            await context.PostAsync("申し訳ありません。" +
                $"「{result.Query})」を理解できませんでした。\n" +
                " 'ヘルプ' または 'help' と入力すると、ヘルプメニューを表示します。");
            context.Done<object>(null);
        }

        [LuisIntent("Help")]
        public async Task Help(IDialogContext context, LuisResult result)
        {
            await context.PostAsync("Help Desk Bot です。" +
                "サポートデスク受付チケットの発行、KB検索ができます。\n\n" +
                "どんなことにお困りですか？例えば「パスワードをリセットしたい」" +
                "「印刷できない」といった文章で入力してください。");
        
            context.Done<object>(null);
        }

        [LuisIntent("SubmitTicket")]
        public async Task SubmitTicket(IDialogContext context, LuisResult result)
        {
            EntityRecommendation categoryEntityRecommendation, severityEntityRecommendation;

            result.TryFindEntity("category", out categoryEntityRecommendation);
            result.TryFindEntity("severity", out severityEntityRecommendation);

            this.category
            = ((Newtonsoft.Json.Linq.JArray)categoryEntityRecommendation?
                .Resolution["values"])?[0]?.ToString();
            this.severity
            = ((Newtonsoft.Json.Linq.JArray)severityEntityRecommendation?
                .Resolution["values"])?[0]?.ToString();
            this.description = result.Query;

            await this.EnsureTicket(context);
        }

        [LuisIntent("ExploreKnowledgeBase")]
        public async Task ExploreCategory(IDialogContext context, LuisResult result)
        {
            EntityRecommendation categoryEntityRecommendation;
            result.TryFindEntity("category", out categoryEntityRecommendation);
            var category =
                ((Newtonsoft.Json.Linq.JArray)categoryEntityRecommendation?
                .Resolution["values"])?[0]?.ToString();
            var originalText = result.Query;

            AzureSearchService searchService = new AzureSearchService();
            if (string.IsNullOrWhiteSpace(category))
            {
                FacetResult facetResult = await searchService.FetchFacets();
                if (facetResult.Facets.Category.Length != 0)
                {
                    List<string> categories = new List<string>();
                    foreach (Category searchedCategory in facetResult.Facets.Category)
                    {
                        categories.Add($"{searchedCategory.Value}({ searchedCategory.Count})");
                    }

                    PromptDialog.Choice(context, this.AfterMenuSelection, categories,
                        "お探しの答えがKBの中にあるか確認しましょう。" +
                        "どのカテゴリーをご覧になりますか？");
                }
            }
            else
            {
                SearchResult searchResult
                    = await searchService.SearchByCategory(category);

                if (searchResult.Value.Length != 0)
                {
                    await context.PostAsync($"{category} には以下のようなKBが" +
                        "見つかりました。" +
                        "**More details** をクリックすると詳細が表示されます。");
                }

                await CardUtil.ShowSearchResults(context, searchResult,
                    $"KBから{category} カテゴリーの記事は見つかりませんでした。");

                context.Done<object>(null);
            }
        }

        public virtual async Task AfterMenuSelection(IDialogContext context,IAwaitable<string> result)
        {
            this.category = await result;
            this.category = System.Text.RegularExpressions.Regex.Replace(this.category, @"\([^)]*\)", string.Empty);
            AzureSearchService searchService = new AzureSearchService();

            SearchResult searchResult
                = await searchService.SearchByCategory(this.category);
            await context.PostAsync($"{this.category}には以下のようなKBが" +
                "見つかりました。" +
                "**More details** をクリックすると詳細が表示されます。");
            await CardUtil.ShowSearchResults(context, searchResult, $"KB から" +
                "{this.category} カテゴリーの記事は見つかりませんでした。");

            context.Done<object>(null);
        }



        private async Task EnsureTicket(IDialogContext context)
        {
            if (this.severity == null)
            {
                var severities = new string[] { "high", "normal", "low" };
                PromptDialog.Choice(context, this.SeverityMessageReceivedAsync,
                severities, "この問題の重要度を選択してください。");
            }
            else if (this.category == null)
            {
                PromptDialog.Text(context, this.CategoryMessageReceivedAsync,
                    "この問題は以下のどのカテゴリーになりますか？\n\n" +
                    "software, hardware, networking, security, other のいずれかを" +
                    "入力してください。");
            }
            else
            {
                var text = $"承知しました。\n\n" +
                    $"重要度: {this.severity}、カテゴリー: {this.category}\n\n" +
                    $"詳細: {this.description} \n\n" +
                    "以上の情報でチケットを発行します。よろしいでしょうか？";

                PromptDialog.Confirm(context,
                    this.IssueConfirmedMessageReceivedAsync, text);
            }
        }


        private async Task SeverityMessageReceivedAsync(IDialogContext context, IAwaitable<string> argument)
        {
            this.severity = await argument;
            await this.EnsureTicket(context);
        }

        private async Task CategoryMessageReceivedAsync(IDialogContext context, IAwaitable<string> argument)
        {
            this.category = await argument;
            await this.EnsureTicket(context);
        }


        public async Task IssueConfirmedMessageReceivedAsync(IDialogContext context, IAwaitable<bool> argument)
        {
            var confirmed = await argument;

            if (confirmed)
            {
                var api = new TicketAPIClient();
                var ticketId = await api.PostTicketAsync(this.category, this.severity, this.description);

                if (ticketId != -1)
                {
                    var message = context.MakeMessage();
                    message.Attachments = new List<Attachment>
                    {
                        new Attachment
                        {
                            ContentType
                                 = "application/vnd.microsoft.card.adaptive",
                            Content = CreateCard(ticketId, this.category,
                                 this.severity, this.description)
                        }
                    };
                    await context.PostAsync(message);
                }
                else
                {
                    await context.PostAsync("サポートチケットの発行中に" +
                        "エラーが発生しました。" +
                        "恐れ入りますが、後ほど再度お試しください");
                }
            }
            else
            {
                await context.PostAsync("サポートチケットの発行を中止しました。" +
                            "サポートチケット発行が必要な場合は再度やり直してください。");

            }
            context.Done<object>(null);
        }

        private AdaptiveCard CreateCard(int ticketId, string category, string severity, string description)
        {
            AdaptiveCard card = new AdaptiveCard();

            var headerBlock = new TextBlock()
            {
                Text = $"Ticket #{ticketId}",
                Weight = TextWeight.Bolder,
                Size = TextSize.Large,
                Speak = $"承知しました。チケットNo. {ticketId} でサポートチケットを" +
                        "発行しました。担当者からの連絡をお待ちください。"
            };

            var columnsBlock = new ColumnSet()
            {
                Separation = SeparationStyle.Strong,
                Columns = new List<Column>
                {
                    new Column
                    {
                        Size = "1",
                        Items = new List<CardElement>
                        {
                            new FactSet
                            {
                                Facts = new List<AdaptiveCards.Fact>
                                {
                                    new AdaptiveCards.Fact("Severity:", severity),
                                    new AdaptiveCards.Fact("Category:", category),
                                }
                            }
                        }
                    },
                    new Column
                    {
                        Size = "auto",
                        Items = new List<CardElement>
                        {
                            new Image
                            {
                                Url ="https://raw.githubusercontent.com/GeekTrainer/help-desk-bot-lab/master/assets/botimages/head-smiling-medium.png",
                                Size = ImageSize.Small,
                                HorizontalAlignment = HorizontalAlignment.Right
                            }
                        }
                    }
                }
            };

            var descriptionBlock = new TextBlock
            {
                Text = description,
                Wrap = true
            };

            card.Body.Add(headerBlock);
            card.Body.Add(columnsBlock);
            card.Body.Add(descriptionBlock);

            return card;
        }

    }
}