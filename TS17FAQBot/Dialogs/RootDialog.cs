namespace HelpDeskBot.Dialogs
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using AdaptiveCards;
    using Microsoft.Bot.Builder.Dialogs;
    using Microsoft.Bot.Builder.Luis;
    using Microsoft.Bot.Builder.Luis.Models;
    using Microsoft.Bot.Connector;
    using Util;
    using HelpDeskBot.Services;
    using HelpDeskBot.Model;
    using System.Text.RegularExpressions;

    [LuisModel("1e5a1f05-4e22-4b49-a68c-09da75f16497", "fef35cf63a1e4bc89244a93744971eb5")]
    [Serializable]
    public class RootDialog : LuisDialog<object>
    {
        private string category;
        private string severity;
        private string description;
        private object searchService;

        [LuisIntent("")]
        [LuisIntent("None")]
        public async Task None(IDialogContext context, LuisResult result)
        {
            await context.PostAsync($"申し訳ありません、「{result.Query}」という入力を理解できませんでした。\n\n" +
                $"別の文章を入力するか、「help」と入力してヘルプメニューを呼び出してください。");
            context.Done<object>(null);
        }

        [LuisIntent("Help")]
        public async Task Help(IDialogContext context, LuisResult result)
        {
            await context.PostAsync("Help Desk Bot です。サポートデスク受付チケットの発行、FAQ検索ができます。\n\n" +
                                    "どんなことにお困りですか？例えば「パスワードをリセットしたい」「印刷できない」といった文章で入力してください。");

            context.Done<object>(null);
        }

        [LuisIntent("SubmitTicket")]
        public async Task SubmitTicket(IDialogContext context, LuisResult result)
        {
            EntityRecommendation categoryEntityRecommendation, severityEntityRecommendation;

            result.TryFindEntity("category", out categoryEntityRecommendation);
            result.TryFindEntity("severity", out severityEntityRecommendation);

            this.category = ((Newtonsoft.Json.Linq.JArray)categoryEntityRecommendation?.Resolution["values"])?[0]?.ToString();
            this.severity = ((Newtonsoft.Json.Linq.JArray)severityEntityRecommendation?.Resolution["values"])?[0]?.ToString();
            this.description = result.Query;

            await this.EnsureTicket(context);
        }

        [LuisIntent("ExploreKnowledgeBase")]
        public async Task ExploreCategory(IDialogContext context, LuisResult result)
        {
            EntityRecommendation categoryEntityRecommendation;
            result.TryFindEntity("category", out categoryEntityRecommendation);
            var category = ((Newtonsoft.Json.Linq.JArray)categoryEntityRecommendation?.Resolution["values"])?[0]?.ToString();
            var originalText = result.Query;

            AzureSearchService searchService = new AzureSearchService();
            if (string.IsNullOrWhiteSpace(category))
            {
                //await context.PostAsync($"例えば「hardwareのFAQを検索」といった文章で入力してください。");

                FacetResult facetResult = await searchService.FetchFacets();
                if (facetResult.Facets.Category.Length != 0)
                {
                    List<string> categories = new List<string>();
                    foreach (Category searchedCategory in facetResult.Facets.Category)
                    {
                        categories.Add($"{searchedCategory.Value} ({searchedCategory.Count})");
                    }

                    PromptDialog.Choice(context, this.AfterMenuSelection, categories, 
                        "お探しの答えがFAQの中にあるか確認しましょう。どのカテゴリーをご覧になりますか？");
                }

                //context.Done<object>(null);
            }
            else
            {
                SearchResult searchResult = await searchService.SearchByCategory(category);
                //string message;
                if (searchResult.Value.Length != 0)
                {
                    //message = $"FAQ から以下のような '{category}' カテゴリーの記事が見つかりました。";
                    //foreach (var item in searchResult.Value)
                    //{
                    //    message += $"\n * {item.Title}";
                    //}

                    await context.PostAsync($"{category}には以下のようなFAQが見つかりました。" +
                        $"**More details** をクリックすると詳細が表示されます。");

                }
                //else
                //{
                //    message = $"FAQ から '{this.category}' カテゴリーの記事は見つかりませんでした。";
                //}

                //await context.PostAsync(message);

                await CardUtil.ShowSearchResults(context, searchResult, $"FAQ から '{category}' カテゴリーの記事は見つかりませんでした。");

                context.Done<object>(null);
            }
        }

        //private async Task ResumeAndEndDialogAsync(IDialogContext context, IAwaitable<object> argument)
        //{
        //    context.Done<object>(null);
        //}

        public virtual async Task AfterMenuSelection(IDialogContext context, IAwaitable<string> result)
        {
            this.category = await result;
            this.category = Regex.Replace(this.category, @"\s\([^)]*\)", string.Empty);
            AzureSearchService searchService = new AzureSearchService();

            SearchResult searchResult = await searchService.SearchByCategory(this.category);
            await context.PostAsync($"{this.category}には以下のようなFAQが見つかりました。" +
                $"**More details** をクリックすると詳細が表示されます。");

            await CardUtil.ShowSearchResults(context, searchResult, $"FAQ から '{this.category}' カテゴリーの記事は見つかりませんでした。");
            context.Done<object>(null);
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
                            ContentType = "application/vnd.microsoft.card.adaptive",
                            Content = this.CreateCard(ticketId, this.category, this.severity, this.description)
                        }
                    };
                    await context.PostAsync(message);
                    await context.PostAsync($"番号:{ticketId} でチケットを発行します。担当者がご連絡しますのでお待ちください。");

                }
                else
                {
                    await context.PostAsync("エラーが発生しました。チケット発行中に問題が発生しました。恐れ入りますが、後ほど再度お試しください。");
                }
            }
            else
            {
                await context.PostAsync("チケットは発行されていません。最初からやり直してください。");
            }

            context.Done<object>(null);
        }

        private async Task EnsureTicket(IDialogContext context)
        {
            if (this.severity == null)
            {
                var severities = new string[] { "high", "normal", "low" };
                PromptDialog.Choice(context, this.SeverityMessageReceivedAsync, severities, "この問題の重要度を選択してください。");
            }
            else if (this.category == null)
            {
                PromptDialog.Text(context, this.CategoryMessageReceivedAsync, "この問題は以下のどのカテゴリーになりますか？ \n\n" +
                    "software, hardware, networking, security, other のいずれかを入力してください。");
            }
            else
            {
                var text = $"承知しました。\n\n重要度: {this.severity}、カテゴリー: {this.category}\n\n" +
                       $"詳細: {this.description} \n\n以上の情報ででチケットを発行します。よろしいでしょうか？";

                PromptDialog.Confirm(context, this.IssueConfirmedMessageReceivedAsync, text);
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

        private AdaptiveCard CreateCard(int ticketId, string category, string severity, string description)
        {
            AdaptiveCard card = new AdaptiveCard();

            var headerBlock = new TextBlock()
            {
                Text = $"Ticket #{ticketId}",
                Weight = TextWeight.Bolder,
                Size = TextSize.Large,
                Speak = $"<s>番号:{ticketId} でチケットを発行します。</s><s>担当者がご連絡しますのでお待ちください。</s>"
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
                                Url = "https://raw.githubusercontent.com/GeekTrainer/help-desk-bot-lab/master/assets/botimages/head-smiling-medium.png",
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