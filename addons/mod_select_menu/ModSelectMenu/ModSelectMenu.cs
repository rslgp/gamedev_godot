using System;
using System.Collections.Generic;
using Godot;

namespace ReusableComponents.ModSelect;

/// <summary>Mod select menu: lists Lua and .NET mods (discovered without running them), lets the player
/// enable/disable each one, filters by name, and saves the selection to <see cref="ConfigPath"/>.
/// It NEVER touches a running game: the host reads the saved selection when it (re)loads mods
/// (see <see cref="LoadSelection"/>). Edits are pending until <see cref="Apply"/>; <see cref="Close"/>
/// discards them.</summary>
[Tool, GlobalClass]
public partial class ModSelectMenu : PanelContainer
{
	/// <summary>Selection saved. Counts are over the discovered mods.</summary>
	[Signal] public delegate void SelectionAppliedEventHandler(int enabledCount, int totalCount);
	/// <summary>Saving failed (file not written; the previous selection stays in effect).</summary>
	[Signal] public delegate void SaveFailedEventHandler(string error);
	/// <summary>Menu closed (pending edits discarded).</summary>
	[Signal] public delegate void ClosedEventHandler();

	string _luaDir = "", _dotnetDir = "", _iface = "", _configPath = "user://mods.cfg";

	/// <summary>Folder holding the Lua load-order file (res://, user:// or absolute). Empty = no Lua mods.</summary>
	[Export] public string LuaModsDir { get => _luaDir; set { _luaDir = value ?? ""; UpdateConfigurationWarnings(); } }
	/// <summary>Folder holding .NET mod DLLs (res://, user:// or absolute). Empty = no .NET mods.</summary>
	[Export] public string DotnetModsDir { get => _dotnetDir; set { _dotnetDir = value ?? ""; UpdateConfigurationWarnings(); } }
	/// <summary>Full name of the interface .NET mods implement (e.g. <c>MyGame.IMod</c>). Required when
	/// <see cref="DotnetModsDir"/> is set.</summary>
	[Export] public string ModInterfaceName { get => _iface; set { _iface = value ?? ""; UpdateConfigurationWarnings(); } }
	/// <summary>Where the selection is saved (default user://mods.cfg).</summary>
	[Export] public string ConfigPath { get => _configPath; set { _configPath = value ?? ""; UpdateConfigurationWarnings(); } }
	/// <summary>Lua load-order file name inside <see cref="LuaModsDir"/>.</summary>
	[Export] public string LoadOrderFile { get; set; } = "load_order.txt";
	/// <summary>Font size used when the menu has no Theme of its own (small viewports).</summary>
	[Export] public int FontSize { get; set; } = 10;
	/// <summary>ui_cancel closes the menu while it is visible.</summary>
	[Export] public bool CloseOnCancel { get; set; } = true;

	/// <summary>False when required exports are missing (an error was pushed; the menu is inert).</summary>
	public bool IsValid { get; private set; }
	/// <summary>Last discovery result (null before the first <see cref="Refresh"/>).</summary>
	public ModCatalog? Catalog { get; private set; }
	/// <summary>Number of rows currently shown by the search filter.</summary>
	public int VisibleCount { get; private set; }

	ModSelection _pending = new();
	LineEdit _search = null!;
	VBoxContainer _list = null!;
	Label _counts = null!, _status = null!;
	Button _apply = null!;
	readonly List<(ModEntry Entry, Control Row, CheckBox Box)> _rows = new();
	bool _built;

	/// <summary>Loads a saved selection for the host's mod loader (missing file = every mod enabled).
	/// Pass <c>IsEnabled</c> of the result as the loader predicate.</summary>
	/// <param name="configPath">res://, user:// or absolute path.</param>
	/// <param name="errors">Optional sink for parse/read problems.</param>
	public static ModSelection LoadSelection(string configPath, List<string>? errors = null) =>
		ModSelection.Load(ProjectSettings.GlobalizePath(configPath), errors);

	/// <inheritdoc/>
	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;
		Build();
		Refresh();
	}

	/// <inheritdoc/>
	public override string[] _GetConfigurationWarnings()
	{
		var w = new List<string>();
		foreach (var p in Problems()) w.Add(p);
		return w.ToArray();
	}

	List<string> Problems()
	{
		var p = new List<string>();
		if (_luaDir.Length == 0 && _dotnetDir.Length == 0) p.Add("LuaModsDir or DotnetModsDir not set");
		if (_dotnetDir.Length > 0 && _iface.Length == 0) p.Add("ModInterfaceName not set (needed to list .NET mods)");
		if (_configPath.Length == 0) p.Add("ConfigPath not set");
		return p;
	}

	void Build()
	{
		if (_built) return;
		_built = true;
		if (Theme == null) Theme = new Theme { DefaultFontSize = FontSize };
		var col = new VBoxContainer();
		AddChild(col);
		var head = new HBoxContainer();
		head.AddChild(new Label { Text = "Mods", SizeFlagsHorizontal = SizeFlags.ExpandFill });
		_counts = new Label();
		head.AddChild(_counts);
		col.AddChild(head);
		_search = new LineEdit { PlaceholderText = "Search mods by name", ClearButtonEnabled = true };
		_search.TextChanged += ApplyFilter;
		col.AddChild(_search);
		var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		_list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		scroll.AddChild(_list);
		col.AddChild(scroll);
		var foot = new HBoxContainer();
		_status = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, Text = "Changes apply on next load / mod reload" };
		foot.AddChild(_status);
		_apply = new Button { Text = "Apply" };
		_apply.Pressed += () => Apply();
		foot.AddChild(_apply);
		var close = new Button { Text = "Close" };
		close.Pressed += Close;
		foot.AddChild(close);
		col.AddChild(foot);
	}

	/// <summary>Re-discovers mods and reloads the saved selection (pending edits are discarded).
	/// Returns false (and pushes an error) when required exports are missing.</summary>
	public bool Refresh()
	{
		if (Engine.IsEditorHint()) return false;
		Build();
		foreach (var r in _rows) { _list.RemoveChild(r.Row); r.Row.QueueFree(); }
		_rows.Clear();
		var problems = Problems();
		IsValid = problems.Count == 0;
		_apply.Disabled = !IsValid;
		if (!IsValid)
		{
			foreach (var p in problems) GD.PushError($"ModSelectMenu: {p}");
			Catalog = null;
			_status.Text = "Not configured: " + string.Join("; ", problems);
			UpdateCounts();
			return false;
		}
		Catalog = ModCatalog.Discover(Abs(_luaDir), Abs(_dotnetDir), _iface, LoadOrderFile);
		foreach (var e in Catalog.Errors) GD.PushWarning($"ModSelectMenu: {e}");
		var errs = new List<string>();
		_pending = LoadSelection(_configPath, errs);
		foreach (var e in errs) GD.PushWarning($"ModSelectMenu: {_configPath}: {e}");
		foreach (var entry in Catalog.Entries) AddRow(entry);
		_status.Text = "Changes apply on next load / mod reload";
		ApplyFilter(_search.Text);
		return true;
	}

	static string? Abs(string p) => p.Length == 0 ? null : ProjectSettings.GlobalizePath(p);

	void AddRow(ModEntry entry)
	{
		var row = new HBoxContainer();
		var box = new CheckBox { Text = entry.Name, ButtonPressed = _pending.IsEnabled(entry.Key), SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = entry.Source };
		var key = entry.Key;
		box.Toggled += on => { _pending.Set(key, on); UpdateCounts(); };
		row.AddChild(box);
		var tag = new Label { Text = entry.Runtime == ModRuntime.Lua ? "Lua" : ".NET" };
		tag.AddThemeColorOverride("font_color", entry.Runtime == ModRuntime.Lua ? new Color(0.55f, 0.75f, 1f) : new Color(0.75f, 0.6f, 1f));
		row.AddChild(tag);
		_list.AddChild(row);
		_rows.Add((entry, row, box));
	}

	void ApplyFilter(string query)
	{
		int n = 0;
		foreach (var r in _rows) { r.Row.Visible = ModCatalog.Matches(r.Entry, query); if (r.Row.Visible) n++; }
		VisibleCount = n;
		UpdateCounts();
	}

	void UpdateCounts()
	{
		if (Catalog == null) { _counts.Text = "0 mods"; return; }
		int lua = 0, luaOn = 0, net = 0, netOn = 0;
		foreach (var e in Catalog.Entries)
		{
			bool on = _pending.IsEnabled(e.Key);
			if (e.Runtime == ModRuntime.Lua) { lua++; if (on) luaOn++; } else { net++; if (on) netOn++; }
		}
		_counts.Text = $"Lua {luaOn}/{lua}  .NET {netOn}/{net}  shown {VisibleCount}";
	}

	/// <summary>Search text (setting it filters the list like typing would).</summary>
	public string Query
	{
		get => _search?.Text ?? "";
		set { Build(); _search.Text = value ?? ""; ApplyFilter(_search.Text); }
	}

	/// <summary>Pending (unsaved) state of one mod.</summary>
	public bool IsPendingEnabled(string key) => _pending.IsEnabled(key);

	/// <summary>Sets a mod on/off as if its checkbox was clicked (pending until <see cref="Apply"/>).
	/// Returns false for an unknown key.</summary>
	public bool SetEnabled(string key, bool enabled)
	{
		foreach (var r in _rows)
		{
			if (r.Entry.Key != key) continue;
			r.Box.ButtonPressed = enabled; // fires Toggled -> pending
			_pending.Set(key, enabled);
			UpdateCounts();
			return true;
		}
		return false;
	}

	/// <summary>Saves the pending selection to <see cref="ConfigPath"/>. Emits SelectionApplied or
	/// SaveFailed. Does not reload anything: the host applies it on its next mod load.</summary>
	public bool Apply()
	{
		if (!IsValid || Catalog == null) return false;
		var save = _pending.Clone();
		save.CaptureAll(Catalog.Entries);
		var err = save.Save(ProjectSettings.GlobalizePath(_configPath));
		if (err != null)
		{
			GD.PushError($"ModSelectMenu: save failed: {err}");
			_status.Text = "Save failed";
			EmitSignal(SignalName.SaveFailed, err);
			return false;
		}
		_pending = save;
		var (on, all) = save.Count(Catalog.Entries);
		_status.Text = $"Saved {on}/{all}. Applies on next load / mod reload";
		EmitSignal(SignalName.SelectionApplied, on, all);
		return true;
	}

	/// <summary>Re-reads mods and saved state, shows the menu and focuses the search field.</summary>
	public void Open()
	{
		Refresh();
		Show();
		_search.GrabFocus();
	}

	/// <summary>Hides the menu and discards unsaved edits (emits Closed).</summary>
	public void Close()
	{
		Hide();
		if (IsValid) _pending = LoadSelection(_configPath);
		foreach (var r in _rows) r.Box.SetPressedNoSignal(_pending.IsEnabled(r.Entry.Key));
		UpdateCounts();
		EmitSignal(SignalName.Closed);
	}

	/// <inheritdoc/>
	public override void _Input(InputEvent ev)
	{
		if (Engine.IsEditorHint() || !CloseOnCancel || !IsVisibleInTree()) return;
		if (ev.IsActionPressed("ui_cancel"))
		{
			GetViewport().SetInputAsHandled();
			Close();
		}
	}
}
