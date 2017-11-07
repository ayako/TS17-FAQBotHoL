using System;
using System.Threading.Tasks;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Connector;
using HelpDeskBot.Util;
using System.Collections.Generic;
using AdaptiveCards;


namespace HelpDeskBot.Dialogs
{
    [Serializable]
    public class RootDialog : IDialog<object>
    {
        private string category;
        private string severity;
        private string description;

        public Task StartAsync(IDialogContext context)
        {
            context.Wait(MessageReceivedAsync);

            return Task.CompletedTask;
        }

        public async Task MessageReceivedAsync
             (IDialogContext context, IAwaitable<IMessageActivity> argument)
        {
            var message = await argument;
            await context.PostAsync("Help Desk Bot です。サポートデスク受付チケットの発行を行います。");
            PromptDialog.Text(context, this.DescriptionMessageReceivedAsync,"どんなことにお困りですか？");
        }

        public async Task DescriptionMessageReceivedAsync
        (IDialogContext context, IAwaitable<string> argument)
        {
            this.description = await argument;
            var severities = new string[] { "high", "normal", "low" };
            PromptDialog.Choice(context, this.SeverityMessageReceivedAsync,severities, "この問題の重要度を入力してください");
        }

        public async Task SeverityMessageReceivedAsync(IDialogContext context, IAwaitable<string> argument)
        {
            this.severity = await argument;
            PromptDialog.Text(context, this.CategoryMessageReceivedAsync,
                "この問題のカテゴリーを以下から選んで入力してください \n\n" +
                "software, hardware, networking, security, other");
        }

        public async Task CategoryMessageReceivedAsync(IDialogContext context, IAwaitable<string> argument)
        {
            this.category = await argument;
            var text = "承知しました。\n\n" +
                $"重要度: \"{this.severity}\"、カテゴリー: \"{this.category}\" " +
                "でサポートチケットを発行します。\n\n" +
                $"詳細: \"{this.description}\" \n\n" +
                "以上の内容で宜しいでしょうか？";

            PromptDialog.Confirm(context,
                this.IssueConfirmedMessageReceivedAsync, text);
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