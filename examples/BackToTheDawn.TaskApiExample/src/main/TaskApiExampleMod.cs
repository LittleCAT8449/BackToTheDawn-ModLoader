using BackToTheDawn.ModAPI;

namespace BackToTheDawn.TaskApiExample;

public sealed class TaskApiExampleMod : IMod
{
    private static readonly ModTaskDefinition[] Quests =
    {
        new(
            "category-mainline",
            "消失的值班记录",
            "医务室的旧档案里出现了两份同一天的值班记录。有人把一名囚犯的名字从公开名单上刮掉，墨迹却透到了背面。查清记录从哪里来，找出被抹掉的人，并把能证明身份的线索交给愿意查案的人。",
            new[]
            {
                // Keep the first objective ID from the earlier category preview so
                // existing saved preview targets can resolve updated localization.
                new ModTaskObjective("check-category", "核对两份值班记录的日期与笔迹，确认被涂改的那一页来自医务室档案。")
                {
                    CompletedDescription = "两份记录确实出自同一本档案，涂改发生在它被送进医务室之后。"
                },
                new ModTaskObjective("identify-prisoner", "从残留的姓氏和牢房编号入手，找出名单上被抹掉的那个人。")
                {
                    CompletedDescription = "你找到了被抹去的名字，也确认他曾被秘密调换过牢房。"
                },
                new ModTaskObjective("deliver-evidence", "整理好能够互相印证的证据，把它交给愿意继续追查的人。")
                {
                    CompletedDescription = "证据已经送出。有人终于开始追问：是谁动了那份名单？"
                },
            })
        {
            Category = ModTaskCategory.Mainline,
            AcceptedDescription = "名单上的空白不像一次普通的笔误。先查清记录本身，再决定下一步。",
            CompletedDescription = "两份记录终于对上了。那个被抹去的人并非凭空消失，背后还有人替他改过档案。",
        },
        new(
            "category-escape",
            "换班表上的空档",
            "看守交接时总有一小段顾不上整条走廊。你需要摸清巡逻节奏，找到通往维修通道的入口，再判断这条路是否真能通向外墙。任何一步出错，都会让整栋监狱提高戒备。",
            new[]
            {
                new ModTaskObjective("check-category", "观察两轮换班，记下巡逻路线、交接顺序和最短的空档。")
                {
                    CompletedDescription = "你记下了巡逻节奏：交接时有一段短暂空档，但不能在同一处停留太久。"
                },
                new ModTaskObjective("find-maintenance-way", "沿着管线和通风口寻找维修通道，确认入口附近有没有锁具或警报。")
                {
                    CompletedDescription = "维修通道确实存在，入口的状况也已经摸清。"
                },
                new ModTaskObjective("prepare-escape", "备好离开所需的物资，并等一个不会惊动整栋监狱的时机。")
                {
                    CompletedDescription = "路线和时机都已准备妥当。接下来只差你决定何时行动。"
                },
            })
        {
            Category = ModTaskCategory.Escape,
            AcceptedDescription = "先别急着行动。一次成功的越狱，靠的是准备，而不是运气。",
            CompletedDescription = "你掌握了换班空档和维修通道的位置，终于有了一条可以认真考虑的路线。",
        },
        new(
            "category-gang",
            "黑账本的缺页",
            "黑市中间人声称，最近几批货的账目被人动过手脚。少掉的那一页不仅记着货物，也记着谁欠了谁的人情。顺着交易记录查下去，别让真正做手脚的人先发现你。",
            new[]
            {
                new ModTaskObjective("check-category", "从最近几笔货物交易入手，找出账本里数量对不上的那一批货。")
                {
                    CompletedDescription = "几笔交易的数量都能对上，唯独同一批货被重复记了一次。"
                },
                new ModTaskObjective("find-missing-page", "查清缺页最后一次出现的位置，并确认是谁有机会接触账本。")
                {
                    CompletedDescription = "账本缺页曾被人带离交易点，接触账本的人也已经缩小到少数几个。"
                },
                new ModTaskObjective("settle-the-ledger", "带着能够自证清白的记录回去交涉，换回缺页或一个明确说法。")
                {
                    CompletedDescription = "缺页的去向已经有了交代。中间人暂时不会再把这笔账算到你头上。"
                },
            })
        {
            Category = ModTaskCategory.Gang,
            AcceptedDescription = "这不是一笔普通的坏账。先把货物流向查明，别急着替任何人背锅。",
            CompletedDescription = "账目重新合上了。有人欠下的不是钱，而是一份迟早要还的人情。",
        },
        new(
            "category-barber-shop",
            "剃刀下的暗号",
            "理发店的价目牌上多了一道不起眼的划痕。老板说那只是木板开裂，可每次划痕变动，店里就会有人来取走一张折起来的纸。查清这套暗号，别把理发店也拖进麻烦里。",
            new[]
            {
                new ModTaskObjective("check-category", "记下价目牌上划痕的位置，观察它和来店客人的先后关系。")
                {
                    CompletedDescription = "划痕不是随手留下的；它的位置对应着一个固定的取件顺序。"
                },
                new ModTaskObjective("identify-recipient", "找出最近一次取走折纸的人，并确认他留下的记号是否一致。")
                {
                    CompletedDescription = "取件人的记号与价目牌上的暗号能对应起来。"
                },
                new ModTaskObjective("protect-the-shop", "把暗号的用途告诉理发店老板，让他决定是否继续替人传递消息。")
                {
                    CompletedDescription = "老板知道了这套暗号的风险，也决定由自己来处理后续的纸条。"
                },
            })
        {
            Category = ModTaskCategory.BarberShop,
            AcceptedDescription = "理发店看起来平常，价目牌上的划痕却不像偶然留下的。先弄清楚它在传什么消息。",
            CompletedDescription = "暗号的来龙去脉已经查清。理发店暂时不用再替陌生人冒险。",
        },
        new(
            "category-prison-guard-captain",
            "队长的夜巡记录",
            "队长的巡夜记录少了最关键的一页。那一夜有人未经登记离开过牢房，记录却显示整条走廊都没有异常。找出记录被抽走的原因，也弄清队长是在掩护谁。",
            new[]
            {
                new ModTaskObjective("check-category", "根据剩下的记录还原当晚的巡逻顺序，找出时间线不可能成立的地方。")
                {
                    CompletedDescription = "巡逻顺序里有一个无法解释的空档，刚好对应那次未登记的离开。"
                },
                new ModTaskObjective("locate-missing-page", "从值班室和交接记录追查缺页，确认它是被藏起来还是被带走了。")
                {
                    CompletedDescription = "缺页被人刻意收走，而不是遗失在值班室里。"
                },
                new ModTaskObjective("question-captain", "拿着还原出的时间线去找队长，听听他愿意为那晚的空档解释什么。")
                {
                    CompletedDescription = "队长终于给出了一个解释。至于该不该相信他，还要看后续证据。"
                },
            })
        {
            Category = ModTaskCategory.PrisonGuardCaptain,
            AcceptedDescription = "夜巡记录不会自己少一页。先还原时间线，再决定要不要直接质问队长。",
            CompletedDescription = "夜巡记录的空档有了说法，但队长的解释仍留下一个无法忽略的疑点。",
        },
        new(
            "category-side",
            "夹在旧书里的纸鹤",
            "有人把一只纸鹤塞进旧书里，纸上只有半句没写完的话。托你找回另一半的人已经离开了原来的牢房。你可以选择把纸鹤交还，也可以先弄明白它为什么不能被别人看见。",
            new[]
            {
                new ModTaskObjective("check-category", "从纸鹤的折痕和落款入手，问出另一半留言现在由谁保管。")
                {
                    CompletedDescription = "另一半留言还在监狱里，只是保管它的人不愿意轻易开口。"
                },
                new ModTaskObjective("learn-the-reason", "确认留言里提到的那件旧事，判断它会给收信人带来什么麻烦。")
                {
                    CompletedDescription = "这封信牵涉到一笔旧债。收信人若被认出来，可能会惹上新的麻烦。"
                },
                new ModTaskObjective("return-the-crane", "决定是否把纸鹤送回去，并把你的选择告诉托付你的人。")
                {
                    CompletedDescription = "纸鹤已经有了归处。无论选择如何，托付你的人都记住了这份情。"
                },
            })
        {
            Category = ModTaskCategory.Side,
            AcceptedDescription = "这只纸鹤看起来不值钱，却有人愿意冒险托你寻找它的另一半。",
            CompletedDescription = "纸鹤的秘密已经揭开。你替一个人完成了迟迟没能说出口的话。",
        },
    };

    private readonly List<IDisposable> _subscriptions = new();
    private ModContext? _context;
    private TaskApi? _tasks;

    public void Initialize(ModContext context)
    {
        _context = context;
        _tasks = TaskApi.For(context);

        foreach (var quest in Quests)
        {
            var registration = _tasks.Register(quest);

            context.Logger.Info(
                $"Quest registration ({quest.Category}): " +
                $"{registration.Status} - {registration.Message}");
        }

        _subscriptions.Add(ModApi.Events.Subscribe<GameplayReadyEvent>(OnGameplayReady));
    }

    public void Shutdown()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
        _context?.Logger.Info("Narrative task example Mod shut down.");
        _context = null;
        _tasks = null;
    }

    private void OnGameplayReady(GameplayReadyEvent _)
    {
        if (_tasks is null || _context is null)
        {
            return;
        }

        foreach (var quest in Quests)
        {
            var result = _tasks.Accept(quest.Id);
            _context.Logger.Info(
                $"Quest acceptance ({quest.Category} / {quest.Name}): " +
                $"{result.Status} - {result.Message}");
        }
    }
}
