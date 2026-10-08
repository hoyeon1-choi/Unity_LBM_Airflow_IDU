using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

public sealed class MainDashboardNavigationController : IDisposable
{
    // Change this single flag when the existing Compare route is ready for release.
    public static bool ComparePageEnabled => false;

    public enum DashboardPage
    {
        Home,
        Simulation,
        Results,
        FmuMonitor,
        Compare,
        Report,
        Settings
    }

    private const string SelectedClassName = "navigation-button--selected";

    private static readonly DashboardPage[] Pages =
    {
        DashboardPage.Home,
        DashboardPage.Simulation,
        DashboardPage.Results,
        DashboardPage.FmuMonitor,
        DashboardPage.Compare,
        DashboardPage.Report,
        DashboardPage.Settings
    };

    private readonly Dictionary<DashboardPage, Button> buttons = new Dictionary<DashboardPage, Button>();
    private readonly Dictionary<DashboardPage, Action> clickHandlers = new Dictionary<DashboardPage, Action>();
    private VisualElement homePage;
    private VisualElement systemMonitorPage;
    private VisualElement timeHistoryPage;
    private VisualElement placeholderPage;
    private Label placeholderTitle;
    private Label placeholderMessage;

    public DashboardPage CurrentPage { get; private set; } = DashboardPage.Home;

    public event Action<DashboardPage> CurrentPageChanged;

    public bool Initialize(VisualElement documentRoot, out string issue)
    {
        if (documentRoot == null)
        {
            issue = "UIDocument 루트가 없습니다.";
            return false;
        }

        homePage = documentRoot.Q<VisualElement>("HomePage");
        systemMonitorPage = documentRoot.Q<VisualElement>("SystemMonitorPage");
        timeHistoryPage = documentRoot.Q<VisualElement>("TimeHistoryPage");
        placeholderPage = documentRoot.Q<VisualElement>("PlaceholderPage");
        placeholderTitle = documentRoot.Q<Label>("PlaceholderPageTitle");
        placeholderMessage = documentRoot.Q<Label>("PlaceholderPageMessage");
        if (homePage == null || systemMonitorPage == null || timeHistoryPage == null || placeholderPage == null ||
            placeholderTitle == null || placeholderMessage == null)
        {
            issue = "PageHost의 Home 또는 Placeholder 요소가 없습니다.";
            return false;
        }

        for (int i = 0; i < Pages.Length; i++)
        {
            DashboardPage page = Pages[i];
            Button button = documentRoot.Q<Button>(GetButtonName(page));
            if (button == null)
            {
                issue = $"Navigation 버튼이 없습니다: {GetButtonName(page)}";
                Dispose();
                return false;
            }

            DashboardPage capturedPage = page;
            Action handler = () => ShowPage(capturedPage);
            buttons.Add(page, button);
            clickHandlers.Add(page, handler);
            button.clicked += handler;
        }

        SetPageEnabled(DashboardPage.Compare, ComparePageEnabled);
        ShowPage(DashboardPage.Home);
        issue = string.Empty;
        return true;
    }

    public bool ShowPage(DashboardPage page)
    {
        if (!buttons.TryGetValue(page, out Button selectedButton) || !selectedButton.enabledSelf)
            return false;

        CurrentPage = page;
        bool showHome = page == DashboardPage.Home;
        bool showSystemMonitor = page == DashboardPage.FmuMonitor;
        bool showTimeHistory = page == DashboardPage.Report;
        bool showPlaceholder = !showHome && !showSystemMonitor && !showTimeHistory;
        homePage.style.display = showHome ? DisplayStyle.Flex : DisplayStyle.None;
        systemMonitorPage.style.display = showSystemMonitor ? DisplayStyle.Flex : DisplayStyle.None;
        timeHistoryPage.style.display = showTimeHistory ? DisplayStyle.Flex : DisplayStyle.None;
        placeholderPage.style.display = showPlaceholder ? DisplayStyle.Flex : DisplayStyle.None;

        for (int i = 0; i < Pages.Length; i++)
        {
            DashboardPage candidate = Pages[i];
            buttons[candidate].EnableInClassList(SelectedClassName, candidate == page);
        }

        if (showPlaceholder)
        {
            placeholderTitle.text = GetPageLabel(page);
            placeholderMessage.text = $"{GetPageLabel(page)} screen is ready for its UX implementation.";
        }

        CurrentPageChanged?.Invoke(page);
        return true;
    }

    public bool SetPageEnabled(DashboardPage page, bool enabled)
    {
        if (!buttons.TryGetValue(page, out Button button))
            return false;

        button.SetEnabled(enabled);
        if (!enabled && CurrentPage == page)
            ShowPage(DashboardPage.Home);
        return true;
    }

    public bool IsPageEnabled(DashboardPage page)
    {
        return buttons.TryGetValue(page, out Button button) && button.enabledSelf;
    }

    public void Dispose()
    {
        foreach (KeyValuePair<DashboardPage, Action> entry in clickHandlers)
        {
            if (buttons.TryGetValue(entry.Key, out Button button))
                button.clicked -= entry.Value;
        }

        clickHandlers.Clear();
        buttons.Clear();
        CurrentPageChanged = null;
    }

    private static string GetButtonName(DashboardPage page)
    {
        return page switch
        {
            DashboardPage.Home => "NavHomeButton",
            DashboardPage.Simulation => "NavSimulationButton",
            DashboardPage.Results => "NavResultsButton",
            DashboardPage.FmuMonitor => "NavFmuMonitorButton",
            DashboardPage.Compare => "NavCompareButton",
            DashboardPage.Report => "NavReportButton",
            DashboardPage.Settings => "NavSettingsButton",
            _ => throw new ArgumentOutOfRangeException(nameof(page), page, null)
        };
    }

    private static string GetPageLabel(DashboardPage page)
    {
        return page switch
        {
            DashboardPage.Home => "Home",
            DashboardPage.Simulation => "Simulation Setup",
            DashboardPage.Results => "Flow Visualization",
            DashboardPage.FmuMonitor => "System Monitor",
            DashboardPage.Compare => "Compare",
            DashboardPage.Report => "Time History",
            DashboardPage.Settings => "Settings",
            _ => throw new ArgumentOutOfRangeException(nameof(page), page, null)
        };
    }
}
