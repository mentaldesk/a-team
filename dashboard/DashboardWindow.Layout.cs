using System.Globalization;
using System.Reflection;
using AssistantPane = MentalDesk.Tui.Assistant.AssistantPane;
using MentalDesk.Tui;
using MentalDesk.Tui.Chrome;
using MentalDesk.Tui.Commands;
using MentalDesk.Tui.Keys;
using MentalDesk.Tui.Links;
using MentalDesk.Tui.Shell;
using Terminal.Gui;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.Text;

namespace ATeam.Dashboard;

public sealed partial class DashboardWindow
{
    private int MenuTitlesEnd() =>
        _menu.Bar.Frame.X + _menu.Bar.SubViews.Where(view => view.Visible).Select(view => view.Frame.Right).DefaultIfEmpty(0).Max();

    private int WorkHeight() => Math.Max(0, Viewport.Height - MenuLines - TitleLines - StatusLines);

    private void Show(Area area)
    {
        if (_area == area)
            return;
        if (_area == Area.Work)
            _left = _work.Place;
        _area = area;
        _settings.WriteArea(area);
        _dispatcherOpen = false;
        if (_sessionFull)
        {
            _sessionFull = false;
            _card.Full = false;
        }
        Arrange();
        _menu.Show(area);
        if (area == Area.Work)
        {
            if (!UpToDate())
                ReadWaiting();
            if (_left is not { } left || !_work.Focus(left))
                _work.FocusFirstCard();
        }
        else
        {
            ReadBoards();
            ShowOverseer(_clock.GetUtcNow());
            _overseer.SetFocus();
        }
        ShowAsking();
        ShowMessage();
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>Claude Code down the right of whichever area is showing, working in the state folder.</summary>
    private AssistantPane NewAssistant(AssistantSetup setup, bool toolCallsInFull)
    {
        var pane = setup.Pane(_stateRoot, action => App?.Invoke(action), () => App, toolCallsInFull);
        Place(pane);
        pane.Visible = false;
        pane.HasFocusChanged += (_, _) => ShowMessage();
        return pane;
    }

    private int AssistantWidth() => _assistant is { Visible: true } && !Tabbed ? Math.Min(AssistantColumns, Viewport.Width / 2) : 0;

    private int BesideAssistant() => Math.Max(0, Viewport.Width - AssistantWidth() - PanelWidth());

    /// <summary>Whether the window is wide enough for the card's panel beside Overseer's lanes.</summary>
    internal static bool Wide(int width) => width >= WideColumns;

    /// <summary>About two fifths of the window.</summary>
    internal static int PanelWidth(int width) => width * 2 / 5;

    /// <summary>The selection's panel is beside the lanes: Overseer, wide, and no log full-screen.</summary>
    private bool Docked => _area == Area.Overseer && Wide(Viewport.Width) && !_sessionFull && !_dispatcherOpen;

    private int PanelWidth() => Docked ? PanelWidth(Viewport.Width) : 0;

    private bool OnHeading => _area == Area.Overseer && _overseer.Board.OnHeading;

    /// <summary>Rows the team's panel takes at the foot of the lanes, where the window is too narrow for it beside them.</summary>
    private int TeamFoot() =>
        OnHeading && !_sessionFull && !_dispatcherOpen && !Wide(Viewport.Width) ? _team.RowsAt(BesideAssistant()) : 0;

    /// <summary>The selection has a panel of its own, beside the lanes or, for a team, at their foot.</summary>
    private bool PanelShown => Docked || TeamFoot() > 0;

    /// <summary>The card's or the team's panel, whichever the selection has.</summary>
    private View Panel => OnHeading ? _team : _card;

    /// <summary>Puts the selection's panel beside the lanes, in a tab beside the Assistant while that's open too, a
    /// team's at their foot where it's narrow, a session over the lanes full-screen, or nowhere; and the Assistant back
    /// on its own wherever they don't share.</summary>
    private void Arrange()
    {
        var tabbed = Docked && _assistant is { Visible: true };
        var panel = Panel;
        var other = panel == _team ? (View)_card : _team;
        View? focused = _assistant is { HasFocus: true } ? _assistant : _card.HasFocus ? _card : _team.HasFocus ? _team : null;
        var assistantFront = tabbed && (_side.Visible ? _side.Value == _assistantTab : _assistant!.HasFocus || !panel.HasFocus);
        Move(other, this);
        Move(panel, tabbed ? _detailsTab : this);
        if (_assistant is not null)
            Move(_assistant, tabbed ? _assistantTab : this);
        if (_side.Visible != tabbed)
        {
            _side.Visible = tabbed;
            if (tabbed)
                _side.Value = assistantFront ? _assistantTab : _detailsTab;
        }
        if (tabbed)
            Fill(panel);
        if (_card.SuperView == this)
        {
            _card.X = _sessionFull ? 0 : Pos.Func(_ => BesideAssistant(), this);
            _card.Y = MenuLines;
            _card.Width = Dim.Func(_ => _sessionFull ? BesideAssistant() : PanelWidth(), this);
            _card.Height = Dim.Func(_ => Math.Max(0, Viewport.Height - MenuLines - StatusLines), this);
        }
        if (_team.SuperView == this)
        {
            _team.X = Pos.Func(_ => Docked ? BesideAssistant() : 0, this);
            _team.Y = Pos.Func(_ => Docked ? MenuLines : Math.Max(0, Viewport.Height - StatusLines - TeamFoot()), this);
            _team.Width = Dim.Func(_ => Docked ? PanelWidth() : BesideAssistant(), this);
            _team.Height = Dim.Func(_ => Docked ? Math.Max(0, Viewport.Height - MenuLines - StatusLines) : TeamFoot(), this);
        }
        var overseer = _area == Area.Overseer && !_dispatcherOpen;
        _card.Visible = overseer && (_sessionFull || (Docked && !OnHeading));
        _team.Visible = overseer && OnHeading && !_sessionFull;
        _overseer.Visible = overseer && !_sessionFull;
        _overseer.Beside = Docked;
        _work.Visible = _title.Visible = _area == Area.Work && !_dispatcherOpen;
        _dispatchFrame.Visible = _dispatcherOpen;
        if (_assistant is not null)
        {
            if (tabbed)
                Fill(_assistant);
            else
                Place(_assistant);
        }
        if (focused is { HasFocus: false, Visible: true } && focused.SuperView is { Visible: true })
        {
            if (focused == _assistant)
                _assistant!.FocusInput();
            else
                focused.SetFocus();
        }
        SetNeedsLayout();
    }

    private static void Move(View view, View into)
    {
        if (view.SuperView == into)
            return;
        view.SuperView?.Remove(view);
        into.Add(view);
    }

    private static void Fill(View view)
    {
        view.X = 0;
        view.Y = 0;
        view.Width = Dim.Fill();
        view.Height = Dim.Fill();
    }

    private void Place(AssistantPane pane)
    {
        pane.X = Pos.Func(_ => Viewport.Width - AssistantWidth(), this);
        pane.Y = MenuLines;
        pane.Width = Dim.Func(_ => AssistantWidth(), this);
        pane.Height = Dim.Func(_ => Math.Max(0, Viewport.Height - MenuLines - StatusLines), this);
    }

    /// <summary>Ctrl+G I: the card's details or the team's roles in front, with focus in them.</summary>
    private void FocusDetails()
    {
        if (!PanelShown)
            return;
        if (Tabbed)
            _side.Value = _detailsTab;
        Panel.SetFocus();
        ShowSelection();
        ShowMessage();
    }

    private bool InAssistant => _assistant is { Visible: true, HasFocus: true };

    /// <summary>A key the question box would type, which mustn't reach a single-letter command instead.</summary>
    private static bool IsTyping(Key key) => !key.IsCtrl && !key.IsAlt && key.AsRune.Value >= ' ';

    /// <summary>Opens the pane with focus in its question box and the agent starting, or closes it. Sharing the panel
    /// with the card's details, it first comes to the front.</summary>
    private void ToggleAssistant()
    {
        if (_assistant is not { } pane)
            return;
        if (Tabbed && _side.Value != _assistantTab)
        {
            _side.Value = _assistantTab;
            pane.FocusInput();
            ShowSelection();
            ShowMessage();
            return;
        }
        pane.Visible = !pane.Visible;
        Arrange();
        if (pane.Visible)
        {
            if (Tabbed)
                _side.Value = _assistantTab;
            ShowAsking();
            pane.FocusInput();
            _ = pane.Controller.EnsureStartedAsync().ContinueWith(started => _ = started.Exception, TaskScheduler.Default);
        }
        else
            FocusArea();
        ShowMessage();
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>Between the area and the panel beside it: the Assistant, or in Overseer whichever of its tabs is in front.</summary>
    private void SwitchPane()
    {
        if (PanelShown && (!Tabbed || _side.Value == _detailsTab))
        {
            if (Panel.HasFocus)
                FocusArea();
            else
                Panel.SetFocus();
            ShowSelection();
        }
        else if (_assistant is not { Visible: true } pane)
            return;
        else if (pane.HasFocus)
            FocusArea();
        else
            pane.FocusInput();
        ShowMessage();
    }

    private void FocusArea()
    {
        if (_dispatcherOpen)
            _dispatchFrame.SetFocus();
        else if (_area == Area.Work)
            _work.SetFocus();
        else if (_sessionFull)
            _card.SetFocus();
        else
            _overseer.SetFocus();
    }

    /// <summary>What the next question is about, above the question box.</summary>
    private void ShowAsking()
    {
        if (_assistant is not { Visible: true } pane)
            return;
        var context = AssistantBrief.Context(_area, AskingAbout(), _teamNames.Count > 1);
        if (pane.NextContext != context)
            pane.NextContext = context;
    }

    private AskedAbout? AskingAbout() =>
        _dispatcherOpen ? new AskedAbout(null, Dispatcher: true)
        : _area == Area.Work ? _work.Selected is { } item ? new AskedAbout(item.Team, item.Number) : _work.Team is { } team ? new AskedAbout(team) : null
        : _overseer.Board.SelectedCard is { } card ? new AskedAbout(card.Team, card.Number)
        : Chosen() is { } watch ? new AskedAbout(watch.Team, Role: watch.Role)
        : _overseer.Board.Selected is { } chip ? new AskedAbout(chip.Team) : null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _assistant?.Controller.Dispose();
        base.Dispose(disposing);
    }
}
