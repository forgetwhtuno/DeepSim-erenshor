using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ErenshorDeepSims
{
    // Retained uGUI only. This editor intentionally keeps generated defaults, authored identity,
    // authored/shared memory, and learned structured memory visually and behaviorally distinct.
    // Empty identity inputs mean "use the deterministic default" and are never populated with the
    // default text, so opening/saving the editor cannot silently promote a template into canon.
    internal static class IdentityEditorUi
    {
        private static readonly Color32 PanelColor = new Color32(4, 23, 32, 246);
        private static readonly Color32 HeaderColor = new Color32(6, 33, 43, 255);
        private static readonly Color32 CellColor = new Color32(8, 40, 52, 238);
        private static readonly Color32 FieldColor = new Color32(3, 20, 27, 245);
        private static readonly Color32 ButtonColor = new Color32(9, 50, 64, 250);
        private static readonly Color32 Cyan = new Color32(143, 224, 255, 255);
        private static readonly Color32 Muted = new Color32(180, 205, 214, 255);
        private static readonly Color32 Authored = new Color32(255, 220, 139, 255);
        private static readonly Color32 Learned = new Color32(170, 224, 178, 255);

        private static DeepSimsPlugin _owner;
        private static GameObject _root;
        private static RectTransform _panel;
        private static Dropdown _simDropdown;
        private static Text _verifiedContext;
        private static Text _defaultSummary;
        private static Text _status;
        private static RectTransform _content;
        private static bool _open;
        private static bool _ignoreDropdown;
        private static int _selectedIndex;
        private static float _nextRosterRefresh;
        private static IdentityEditorModel _model;
        private static IdentityEditorDraft _draft;
        private static readonly List<SimSnapshot> Sims = new List<SimSnapshot>();
        private static readonly Dictionary<string, InputField> IdentityInputs = new Dictionary<string, InputField>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Toggle> ShareToggles = new Dictionary<string, Toggle>(StringComparer.OrdinalIgnoreCase);
        private static InputField _memoryInput;
        private static Text _memoryModeText;
        private static string _editingRecordId = string.Empty;
        private static IdentityMemorySection _editingSection = IdentityMemorySection.PinnedAuthored;
        private static bool _legacyPointerOwned;
        private static bool _legacyDraggingBefore;
        private static string _pendingDestructiveAction = string.Empty;
        private static float _pendingDestructiveUntil;

        internal static bool IsOpen { get { return _open; } }
        internal static bool OwnsCameraInput
        {
            get
            {
                if (!_open || _root == null || !_root.activeInHierarchy) return false;
                try
                {
                    if (_panel != null && RectTransformUtility.RectangleContainsScreenPoint(_panel, Input.mousePosition, null)) return true;
                    GameObject selected = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
                    return selected != null && IsUnderRoot(selected.transform);
                }
                catch { return false; }
            }
        }

        internal static void Initialize(DeepSimsPlugin owner)
        {
            Dispose();
            _owner = owner;
        }

        internal static bool Open() { return Open(null); }

        internal static bool Open(SimSnapshot preferred)
        {
            if (_owner == null) return false;
            _open = true;
            RefreshRoster(true);
            if (preferred != null && !string.IsNullOrWhiteSpace(preferred.Key))
                for (int i = 0; i < Sims.Count; i++)
                    if (Sims[i] != null && string.Equals(Sims[i].Key, preferred.Key, StringComparison.OrdinalIgnoreCase)) { _selectedIndex = i; break; }
            EnsureBuilt();
            if (_root != null)
            {
                _root.SetActive(true);
                RebuildSelectorAndContent();
            }
            return true;
        }

        internal static void Close()
        {
            _open = false;
            ReleaseLegacyPointerOwnership();
            ClearPendingConfirmation();
            if (_root != null) _root.SetActive(false);
        }

        internal static void OnCharacterScopeChanged()
        {
            // A selected Sim/memory model belongs to exactly one player-character scope. Closing is
            // safer than carrying a draft across the verified character boundary.
            Close();
            Sims.Clear();
            _model = null;
            _draft = null;
            _selectedIndex = 0;
            _editingRecordId = string.Empty;
            if (_root != null) RebuildSelectorAndContent();
        }

        internal static void Tick(bool gameplayReady)
        {
            if (!_open) { if (_root != null) _root.SetActive(false); return; }
            if (!gameplayReady || EventSystem.current == null)
            {
                if (_root != null) _root.SetActive(false);
                return;
            }
            if (!EnsureBuilt()) return;
            _root.SetActive(true);
            UpdateInputOwnership();
            if (Time.unscaledTime >= _nextRosterRefresh)
            {
                _nextRosterRefresh = Time.unscaledTime + 1.25f;
                RefreshRoster(false);
            }
        }

        internal static void Dispose()
        {
            ReleaseLegacyPointerOwnership();
            ClearPendingConfirmation();
            if (_root != null) DestroyRuntime(_root);
            _root = null; _panel = null; _simDropdown = null; _verifiedContext = null; _defaultSummary = null; _status = null; _content = null;
            _open = false; _ignoreDropdown = false; _selectedIndex = 0; _nextRosterRefresh = 0f; _model = null; _draft = null; _owner = null;
            _memoryInput = null; _memoryModeText = null; _editingRecordId = string.Empty;
            Sims.Clear(); IdentityInputs.Clear(); ShareToggles.Clear();
        }

        private static bool EnsureBuilt()
        {
            if (_root != null) return true;
            if (EventSystem.current == null) return false;
            try
            {
                _root = new GameObject("ForgottenRoads.DeepSims.IdentityEditor", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                UnityEngine.Object.DontDestroyOnLoad(_root);
                Canvas canvas = _root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                // The editor is an owned modal surface. Explicit override sorting keeps a retained
                // Suite navigation layer from painting through the top identity row.
                canvas.overrideSorting = true; canvas.sortingOrder = 800;
                CanvasScaler scaler = _root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920f, 1080f); scaler.matchWidthOrHeight = 0.5f;

                GameObject panelGo = MakeRect("Panel", _root.transform, Vector2.zero);
                _panel = panelGo.GetComponent<RectTransform>();
                // Resolution-independent retained layout: the panel owns 92% x 90% of the canvas
                // rather than assuming a 1040x860 desktop. With CanvasScaler this preserves sane
                // margins at 1280x720 through ultrawide/high-DPI resolutions.
                SetAnchors(_panel, new Vector2(.04f, .05f), new Vector2(.96f, .95f), 0f, 0f, 0f, 0f);
                panelGo.AddComponent<Image>().color = PanelColor;

                RectTransform header = MakeRect("Header", _panel, Vector2.zero).GetComponent<RectTransform>();
                SetTopBand(header, 0f, 42f, 0f, 0f); header.gameObject.AddComponent<Image>().color = HeaderColor;
                Text title = MakeText(header, "DEEP SIMS — IDENTITY & MEMORY", 18, TextAnchor.MiddleLeft, Cyan); SetInset(title.rectTransform, 12f, 0f, 56f, 0f);
                Button close = MakeButton(header, "X", new Vector2(38f, 30f), Vector2.zero, delegate { Close(); });
                RectTransform closeRect = close.GetComponent<RectTransform>(); closeRect.anchorMin = closeRect.anchorMax = new Vector2(1f, .5f); closeRect.pivot = new Vector2(1f, .5f); closeRect.anchoredPosition = new Vector2(-8f, 0f);

                RectTransform selectorRow = MakeRect("Selector", _panel, Vector2.zero).GetComponent<RectTransform>();
                SetTopBand(selectorRow, 50f, 42f, 13f, 13f);
                RectTransform simLabelRect = MakeChild("SimLabel", selectorRow, new Vector2(44f, 42f), new Vector2(0f, 0f));
                MakeText(simLabelRect, "SIM", 12, TextAnchor.MiddleLeft, Muted);
                _simDropdown = MakeDropdown(selectorRow, new Vector2(300f, 34f), new Vector2(48f, 4f));
                _simDropdown.onValueChanged.AddListener(delegate(int index) { if (!_ignoreDropdown) SelectIndex(index); });
                MakeButton(selectorRow, "Refresh", new Vector2(82f, 32f), new Vector2(356f, 5f), delegate { RefreshRoster(true); });
                MakeButton(selectorRow, "Save", new Vector2(78f, 32f), new Vector2(446f, 5f), SaveIdentity);
                MakeButton(selectorRow, "Reset All", new Vector2(94f, 32f), new Vector2(532f, 5f), ResetAllIdentity);
                MakeButton(selectorRow, "Reload", new Vector2(82f, 32f), new Vector2(634f, 5f), ReloadSelected);
                MakeButton(selectorRow, "Export", new Vector2(72f, 32f), new Vector2(724f, 5f), ExportIdentity);
                MakeButton(selectorRow, "Import", new Vector2(72f, 32f), new Vector2(804f, 5f), ImportIdentity);

                RectTransform verified = MakeRect("VerifiedContext", _panel, Vector2.zero).GetComponent<RectTransform>();
                SetTopBand(verified, 98f, 44f, 13f, 13f); verified.gameObject.AddComponent<Image>().color = new Color32(3, 25, 34, 220);
                _verifiedContext = MakeText(verified, string.Empty, 11, TextAnchor.MiddleLeft, Cyan); SetInset(_verifiedContext.rectTransform, 8f, 4f, 8f, 4f);
                _defaultSummary = null;

                RectTransform viewport = MakeRect("Viewport", _panel, Vector2.zero).GetComponent<RectTransform>();
                // Header (42), selector (42) and VERIFIED NOW (44) occupy the top 142 pixels.
                // Reserve a real module inset plus a gap rather than letting the first scroll row
                // share that boundary with Suite controls at any supported resolution.
                SetAnchors(viewport, Vector2.zero, Vector2.one, 13f, 56f, 13f, 162f);
                Image vpImage = viewport.gameObject.AddComponent<Image>(); vpImage.color = new Color32(0, 0, 0, 50);
                // This modal owns every scrolling graphic below Content, including each field's
                // background and Reset button. RectMask2D clips those retained descendants at the
                // viewport rectangle without relying on stencil material inheritance, which could
                // leave a fragment of a departing field painted beneath the fixed VERIFIED NOW row.
                viewport.gameObject.AddComponent<RectMask2D>();
                ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.vertical = true; scroll.scrollSensitivity = 32f;
                _content = MakeRect("Content", viewport, Vector2.zero).GetComponent<RectTransform>();
                _content.anchorMin = new Vector2(0f, 1f); _content.anchorMax = new Vector2(1f, 1f); _content.pivot = new Vector2(.5f, 1f);
                _content.offsetMin = new Vector2(0f, 0f); _content.offsetMax = new Vector2(0f, 0f); _content.anchoredPosition = Vector2.zero;
                VerticalLayoutGroup layout = _content.gameObject.AddComponent<VerticalLayoutGroup>(); layout.padding = new RectOffset(8, 8, 8, 8); layout.spacing = 8f; layout.childControlHeight = false; layout.childControlWidth = true; layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;
                ContentSizeFitter fitter = _content.gameObject.AddComponent<ContentSizeFitter>(); fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                scroll.viewport = viewport; scroll.content = _content;

                RectTransform status = MakeRect("Status", _panel, Vector2.zero).GetComponent<RectTransform>();
                SetBottomBand(status, 8f, 40f, 13f, 13f);
                _status = MakeText(status, "Generated fictional player backgrounds stay separate from authored overrides and verified Erenshor facts.", 10, TextAnchor.MiddleLeft, Muted);
                RebuildSelectorAndContent();
                _root.SetActive(_open);
                return true;
            }
            catch
            {
                if (_root != null) DestroyRuntime(_root);
                _root = null;
                return false;
            }
        }

        private static void RefreshRoster(bool force)
        {
            if (_owner == null) return;
            List<SimSnapshot> fresh = _owner.GetIdentityEditorSims();
            bool changed = force || fresh.Count != Sims.Count;
            if (!changed)
            {
                for (int i = 0; i < fresh.Count; i++)
                    if (fresh[i] == null || Sims[i] == null || !string.Equals(fresh[i].Key, Sims[i].Key, StringComparison.OrdinalIgnoreCase)) { changed = true; break; }
            }
            if (!changed)
            {
                for (int i = 0; i < fresh.Count && i < Sims.Count; i++) Sims[i] = fresh[i];
                RefreshLiveHeader();
                return;
            }

            string oldKey = CurrentSim() == null ? string.Empty : (CurrentSim().Key ?? string.Empty);
            Sims.Clear();
            for (int i = 0; i < fresh.Count; i++) if (fresh[i] != null) Sims.Add(fresh[i]);
            _selectedIndex = 0;
            if (!string.IsNullOrWhiteSpace(oldKey))
                for (int i = 0; i < Sims.Count; i++) if (string.Equals(Sims[i].Key, oldKey, StringComparison.OrdinalIgnoreCase)) { _selectedIndex = i; break; }
            if (_selectedIndex >= Sims.Count) _selectedIndex = Math.Max(0, Sims.Count - 1);
            if (_root != null) RebuildSelectorAndContent();
        }

        private static void RebuildSelectorAndContent()
        {
            if (_simDropdown != null)
            {
                _ignoreDropdown = true;
                _simDropdown.ClearOptions();
                List<string> names = new List<string>();
                for (int i = 0; i < Sims.Count; i++) names.Add(Sims[i] == null ? "Unknown Sim" : Sims[i].Name);
                if (names.Count == 0) names.Add("No active local Deep Sims");
                _simDropdown.AddOptions(names); _simDropdown.value = Math.Min(_selectedIndex, names.Count - 1); _simDropdown.RefreshShownValue();
                _simDropdown.interactable = Sims.Count > 0;
                _ignoreDropdown = false;
            }
            ReloadModel();
        }

        private static void SelectIndex(int index)
        {
            if (Sims.Count == 0) return;
            _selectedIndex = Mathf.Clamp(index, 0, Sims.Count - 1);
            ClearPendingConfirmation();
            ReloadModel();
        }

        private static void RefreshLiveHeader()
        {
            SimSnapshot sim = CurrentSim();
            if (sim == null || _owner == null) return;
            IdentityEditorModel live = _owner.GetIdentityEditorModel(sim);
            if (live == null) return;
            if (_verifiedContext != null) _verifiedContext.text = "VERIFIED NOW  •  " + live.VerifiedContext;
            if (_defaultSummary != null) _defaultSummary.text = "DEFAULT PROFILE  •  " + live.DefaultSummary + "\nGenerated locally and not persisted. Authored overrides remain separate.";
        }

        private static void ReloadSelected() { ReloadModel(); SetStatus("Reloaded selected Sim. Unsaved identity edits were discarded.", false); }

        private static void ReloadModel()
        {
            IdentityInputs.Clear(); ShareToggles.Clear(); _editingRecordId = string.Empty;
            SimSnapshot sim = CurrentSim();
            if (sim == null || _owner == null)
            {
                _model = null; _draft = null;
                if (_verifiedContext != null) _verifiedContext.text = "No active local Deep Sim is available.";
                if (_defaultSummary != null) _defaultSummary.text = "Default identity is generated only for a verified Sim selection.";
                ClearContent();
                return;
            }
            _model = _owner.GetIdentityEditorModel(sim); _draft = new IdentityEditorDraft(_model);
            if (_verifiedContext != null) _verifiedContext.text = "VERIFIED NOW  •  " + (_model == null ? string.Empty : _model.VerifiedContext);
            if (_defaultSummary != null) _defaultSummary.text = "DEFAULT PROFILE  •  " + (_model == null ? string.Empty : _model.DefaultSummary) + "\nGenerated locally and not persisted. Authored overrides remain separate.";
            BuildContent();
        }

        private static void BuildContent()
        {
            ClearContent();
            if (_content == null || _model == null || _draft == null) return;
            AddSectionHeader("SIMULATED PLAYER / ROLEPLAY IDENTITY");
            AddIdentityField(_model.Background);
            AddIdentityField(_model.Personality);
            AddIdentityField(_model.Persona);
            AddIdentityField(_model.Relationship);
            AddIdentityField(_model.Wants);
            AddIdentityField(_model.Cares);

            AddSectionHeader("MEMORY AUTHORING");
            AddMemoryComposer();
            List<IdentityMemoryView> authored = new List<IdentityMemoryView>();
            authored.AddRange(_model.Pinned); authored.AddRange(_model.AuthoredHistory);
            AddMemorySection("PINNED / AUTHORED", authored, Authored);
            AddMemorySection("SHARED HISTORY", _model.SharedHistory, Authored);
            AddMemorySection("LEARNED MEMORY", _model.Learned, Learned);
            if (_content != null) _content.anchoredPosition = Vector2.zero;
        }

        private static void AddIdentityField(IdentityFieldView view)
        {
            if (view == null) return;
            RectTransform group = AddLayoutBlock("IdentityField-" + view.Key, 150f, CellColor);

            RectTransform labelRow = MakeRect("LabelRow", group, Vector2.zero).GetComponent<RectTransform>();
            SetAnchors(labelRow, new Vector2(0f, 1f), new Vector2(1f, 1f), 8f, -30f, 8f, 0f);
            Text label = MakeText(labelRow, view.Label, 13, TextAnchor.MiddleLeft, view.Source == IdentityValueSource.AuthoredOverride ? Authored : Cyan);
            label.fontStyle = FontStyle.Bold; SetInset(label.rectTransform, 0f, 0f, 132f, 0f);
            Text source = MakeText(labelRow, view.SourceLabel, 10, TextAnchor.MiddleRight, view.Source == IdentityValueSource.AuthoredOverride ? Authored : Muted);
            source.fontStyle = FontStyle.Bold;

            RectTransform defaultRow = MakeRect("Default", group, Vector2.zero).GetComponent<RectTransform>();
            SetAnchors(defaultRow, new Vector2(0f, 1f), new Vector2(1f, 1f), 8f, -58f, 8f, 30f);
            string defaultPrefix = view.Source == IdentityValueSource.PersistedGeneratedDefault ? "Generated: " : "Default: ";
            Text defaultText = MakeText(defaultRow, defaultPrefix + Bound(view.DefaultText, 320), 10, TextAnchor.UpperLeft, Muted);
            defaultText.horizontalOverflow = HorizontalWrapMode.Wrap;

            InputField input = MakeInput(group, new Vector2(100f, 78f), Vector2.zero, true);
            RectTransform inputRect = input.GetComponent<RectTransform>();
            SetAnchors(inputRect, new Vector2(0f, 0f), new Vector2(1f, 0f), 8f, 8f, 106f, -86f);
            input.text = _draft.Get(view.Key);
            string key = view.Key; input.onValueChanged.AddListener(delegate(string value) { if (_draft != null) _draft.Set(key, value); });
            IdentityInputs[view.Key] = input;

            Button reset = MakeButton(group, "Reset", new Vector2(86f, 30f), Vector2.zero, delegate { ResetIdentityField(key); });
            RectTransform resetRect = reset.GetComponent<RectTransform>(); resetRect.anchorMin = resetRect.anchorMax = new Vector2(1f, 0f); resetRect.pivot = new Vector2(1f, 0f); resetRect.anchoredPosition = new Vector2(-10f, 10f);
        }

        private static void AddMemoryComposer()
        {
            RectTransform block = AddLayoutBlock("MemoryComposer", 190f, CellColor);
            _memoryModeText = MakeText(MakeChild("Mode", block, new Vector2(930f, 24f), new Vector2(8f, 157f)), "New authored memory — learned records cannot be edited in place.", 11, TextAnchor.MiddleLeft, Muted);
            _memoryInput = MakeInput(block, new Vector2(930f, 66f), new Vector2(8f, 84f), true);
            Text memoryPlaceholder = _memoryInput.placeholder as Text;
            if (memoryPlaceholder != null) memoryPlaceholder.text = "Enter an explicit authored memory. This will not be stored as learned memory.";
            MakeButton(block, "+ Add Pinned", new Vector2(108f, 30f), new Vector2(8f, 46f), delegate { AddAuthoredMemory("pinned"); });
            MakeButton(block, "+ Add History", new Vector2(108f, 30f), new Vector2(124f, 46f), delegate { AddAuthoredMemory("history"); });
            MakeButton(block, "Save Edit", new Vector2(94f, 30f), new Vector2(240f, 46f), SaveMemoryEdit);
            MakeButton(block, "Cancel Edit", new Vector2(94f, 30f), new Vector2(342f, 46f), CancelMemoryEdit);
            MakeButton(block, "+ Add Shared History", new Vector2(150f, 30f), new Vector2(444f, 46f), AddSharedHistory);

            MakeText(MakeChild("ShareLabel", block, new Vector2(930f, 20f), new Vector2(8f, 22f)), "Shared participants (selected Sim is always included):", 10, TextAnchor.MiddleLeft, Muted);
            float x = 320f;
            SimSnapshot selected = CurrentSim();
            for (int i = 0; i < Sims.Count && i < 7; i++)
            {
                SimSnapshot sim = Sims[i]; if (sim == null) continue;
                Toggle toggle = MakeToggle(block, Bound(sim.Name, 18), new Vector2(128f, 22f), new Vector2(x, 20f));
                bool isSelected = selected != null && string.Equals(selected.Key, sim.Key, StringComparison.OrdinalIgnoreCase);
                toggle.isOn = isSelected; toggle.interactable = !isSelected;
                ShareToggles[sim.Key ?? sim.Name] = toggle; x += 132f;
                if (x > 900f) break;
            }
        }

        private static void AddMemorySection(string title, List<IdentityMemoryView> records, Color32 color)
        {
            AddSectionHeader(title + "  (" + (records == null ? 0 : records.Count) + ")");
            if (records == null || records.Count == 0)
            {
                RectTransform empty = AddLayoutBlock("Empty", 42f, FieldColor);
                MakeText(empty, "None.", 11, TextAnchor.MiddleLeft, Muted);
                return;
            }
            for (int i = 0; i < records.Count; i++) AddMemoryRow(records[i], color);
        }

        private static void AddMemoryRow(IdentityMemoryView view, Color32 color)
        {
            if (view == null) return;
            RectTransform row = AddLayoutBlock("Memory-" + view.Id, 96f, FieldColor);
            string meta = view.Source + "  •  type: " + (string.IsNullOrWhiteSpace(view.Type) ? "memory" : view.Type) + "  •  known by: " + view.KnownBy;
            if (!string.IsNullOrWhiteSpace(view.Utc)) meta += "  •  " + Bound(view.Utc, 28);
            Text main = MakeText(MakeChild("Text", row, new Vector2(740f, 56f), new Vector2(8f, 34f)), view.Text, 11, TextAnchor.UpperLeft, color); main.horizontalOverflow = HorizontalWrapMode.Wrap;
            MakeText(MakeChild("Meta", row, new Vector2(740f, 26f), new Vector2(8f, 6f)), meta, 9, TextAnchor.MiddleLeft, Muted);
            float y = 54f;
            if (view.CanEdit) { IdentityMemoryView captured = view; MakeButton(row, "Edit", new Vector2(78f, 28f), new Vector2(770f, y), delegate { BeginMemoryEdit(captured); }); y -= 34f; }
            if (view.CanRemove) { IdentityMemoryView captured = view; MakeButton(row, "Remove", new Vector2(78f, 28f), new Vector2(856f, 54f), delegate { RemoveMemory(captured); }); }
            if (view.CanForget) { IdentityMemoryView captured = view; MakeButton(row, "Forget", new Vector2(78f, 28f), new Vector2(770f, 54f), delegate { ForgetLearned(captured); }); }
            if (view.CanCopyToAuthored) { IdentityMemoryView captured = view; MakeButton(row, "Copy → Pinned", new Vector2(126f, 28f), new Vector2(856f, 54f), delegate { CopyLearned(captured); }); }
        }

        private static void SaveIdentity()
        {
            SimSnapshot sim = CurrentSim(); if (_owner == null || sim == null || _draft == null) return;
            string result; bool ok = _owner.TrySaveIdentityEditor(sim, _draft.ToProfile(), out result); SetStatus(result, ok); if (ok) ReloadModel();
        }

        private static void ResetIdentityField(string key)
        {
            SimSnapshot sim = CurrentSim(); if (_owner == null || sim == null) return;
            string result; bool ok = _owner.TryResetIdentityFieldEditor(sim, key, out result); SetStatus(result, ok); if (ok) ReloadModel();
        }

        private static void ResetAllIdentity()
        {
            SimSnapshot sim = CurrentSim(); if (_owner == null || sim == null) return;
            string key = "reset-all:" + (sim.Key ?? sim.Name ?? string.Empty);
            ConfirmDestructive(key, "Reset ALL authored identity fields for " + (sim.Name ?? "this Sim") + "?", delegate
            {
                string result; bool ok = _owner.TryResetAllIdentityEditor(sim, out result); SetStatus(result, ok); if (ok) ReloadModel();
            });
        }

        private static void AddAuthoredMemory(string kind)
        {
            SimSnapshot sim = CurrentSim(); if (_owner == null || sim == null || _memoryInput == null) return;
            string result; bool ok = _owner.TryAddIdentityMemoryEditor(sim, kind, _memoryInput.text, out result); SetStatus(result, ok); if (ok) ReloadModel();
        }

        private static void AddSharedHistory()
        {
            if (_owner == null || _memoryInput == null) return;
            SimSnapshot selected = CurrentSim(); if (selected == null) return;
            List<SimSnapshot> knowers = new List<SimSnapshot>(); knowers.Add(selected);
            for (int i = 0; i < Sims.Count; i++)
            {
                SimSnapshot sim = Sims[i]; if (sim == null || string.Equals(sim.Key, selected.Key, StringComparison.OrdinalIgnoreCase)) continue;
                Toggle toggle; if (ShareToggles.TryGetValue(sim.Key ?? sim.Name, out toggle) && toggle != null && toggle.isOn) knowers.Add(sim);
            }
            if (knowers.Count < 2) { SetStatus("Select at least one additional verified Sim for shared history.", false); return; }
            string result; bool ok = _owner.TryAddSharedHistoryEditor(knowers, _memoryInput.text, out result); SetStatus(result, ok); if (ok) ReloadModel();
        }

        private static void BeginMemoryEdit(IdentityMemoryView view)
        {
            if (view == null || !view.CanEdit || _memoryInput == null) return;
            _editingRecordId = view.Id ?? string.Empty; _editingSection = view.Section; _memoryInput.text = view.Text ?? string.Empty;
            if (_memoryModeText != null) _memoryModeText.text = "Editing authored " + view.Section + ". KnownBy/source metadata is preserved.";
        }

        private static void CancelMemoryEdit()
        {
            _editingRecordId = string.Empty; if (_memoryInput != null) _memoryInput.text = string.Empty;
            if (_memoryModeText != null) _memoryModeText.text = "New authored memory — learned records cannot be edited in place.";
        }

        private static void SaveMemoryEdit()
        {
            SimSnapshot sim = CurrentSim(); if (_owner == null || sim == null || _memoryInput == null) return;
            if (string.IsNullOrWhiteSpace(_editingRecordId)) { SetStatus("Choose Edit on an authored memory first.", false); return; }
            string result; bool ok = _owner.TryUpdateIdentityMemoryEditor(sim, _editingSection, _editingRecordId, _memoryInput.text, out result); SetStatus(result, ok); if (ok) ReloadModel();
        }

        private static void RemoveMemory(IdentityMemoryView view)
        {
            SimSnapshot sim = CurrentSim(); if (_owner == null || sim == null || view == null) return;
            ConfirmDestructive("remove:" + (view.Id ?? string.Empty), "Remove this authored/shared memory?", delegate
            {
                string result; bool ok = _owner.TryRemoveIdentityMemoryEditor(sim, view.Section, view.Id, out result); SetStatus(result, ok); if (ok) ReloadModel();
            });
        }

        private static void ForgetLearned(IdentityMemoryView view)
        {
            SimSnapshot sim = CurrentSim(); if (_owner == null || sim == null || view == null) return;
            ConfirmDestructive("forget:" + (view.Id ?? string.Empty), "Forget this learned memory?", delegate
            {
                string result; bool ok = _owner.TryForgetLearnedMemoryEditor(sim, view.Id, out result); SetStatus(result, ok); if (ok) ReloadModel();
            });
        }

        private static void CopyLearned(IdentityMemoryView view)
        {
            SimSnapshot sim = CurrentSim(); if (_owner == null || sim == null || view == null) return;
            string result; bool ok = _owner.TryCopyLearnedMemoryEditor(sim, view.Id, out result); SetStatus(result, ok); if (ok) ReloadModel();
        }

        private static void ExportIdentity()
        {
            SimSnapshot sim = CurrentSim(); if (_owner == null || sim == null) return;
            string result; bool ok = _owner.TryExportIdentityEditor(sim, out result); SetStatus(result, ok);
        }

        private static void ImportIdentity()
        {
            SimSnapshot sim = CurrentSim(); if (_owner == null || sim == null) return;
            ConfirmDestructive("import:" + (sim.Key ?? sim.Name ?? string.Empty), "Import will replace authored identity/profile data for this Sim. Continue?", delegate
            {
                string result; bool ok = _owner.TryImportIdentityEditor(sim, out result); SetStatus(result, ok); if (ok) ReloadModel();
            });
        }

        private static SimSnapshot CurrentSim() { return Sims.Count == 0 || _selectedIndex < 0 || _selectedIndex >= Sims.Count ? null : Sims[_selectedIndex]; }
        private static void SetStatus(string text, bool ok) { if (_status != null) { _status.text = text ?? string.Empty; _status.color = ok ? Learned : new Color32(255, 188, 130, 255); } }

        private static void ClearContent()
        {
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--)
            {
                GameObject child = null; try { child = _content.GetChild(i).gameObject; } catch { }
                if (child != null) DestroyRuntime(child);
            }
            IdentityInputs.Clear(); ShareToggles.Clear(); _memoryInput = null; _memoryModeText = null;
        }

        private static void ConfirmDestructive(string actionKey, string prompt, Action execute)
        {
            float now = Time.unscaledTime;
            if (!string.Equals(_pendingDestructiveAction, actionKey ?? string.Empty, StringComparison.Ordinal) || now > _pendingDestructiveUntil)
            {
                _pendingDestructiveAction = actionKey ?? string.Empty;
                _pendingDestructiveUntil = now + 4f;
                SetStatus((prompt ?? "Confirm destructive action.") + " Click the same action again within 4 seconds to confirm.", false);
                return;
            }
            ClearPendingConfirmation();
            try { if (execute != null) execute(); } catch { SetStatus("The confirmed action failed before completion.", false); }
        }

        private static void ClearPendingConfirmation()
        {
            _pendingDestructiveAction = string.Empty;
            _pendingDestructiveUntil = 0f;
        }

        private static bool IsUnderRoot(Transform transform)
        {
            if (transform == null || _root == null) return false;
            Transform cursor = transform;
            while (cursor != null)
            {
                if (cursor == _root.transform) return true;
                cursor = cursor.parent;
            }
            return false;
        }

        private static void UpdateInputOwnership()
        {
            bool leftHeld = false;
            try { leftHeld = Input.GetMouseButton(0); } catch { }
            bool shouldOwnLegacy = leftHeld && OwnsCameraInput;
            if (shouldOwnLegacy && !_legacyPointerOwned)
            {
                try { _legacyDraggingBefore = GameData.DraggingUIElement; } catch { _legacyDraggingBefore = false; }
                _legacyPointerOwned = true;
            }
            if (_legacyPointerOwned)
            {
                if (shouldOwnLegacy)
                {
                    try { if (!GameData.DraggingUIElement) GameData.DraggingUIElement = true; } catch { }
                }
                else ReleaseLegacyPointerOwnership();
            }
        }

        private static void ReleaseLegacyPointerOwnership()
        {
            if (!_legacyPointerOwned) return;
            try { GameData.DraggingUIElement = _legacyDraggingBefore; } catch { }
            _legacyPointerOwned = false;
            _legacyDraggingBefore = false;
        }

        private static void DestroyRuntime(GameObject go)
        {
            if (go == null) return;
            try { go.SetActive(false); } catch { }
            try { UnityEngine.Object.Destroy(go); } catch { }
        }

        private static void AddSectionHeader(string text)
        {
            RectTransform block = AddLayoutBlock("Section", 30f, HeaderColor);
            Text t = MakeText(block, text, 12, TextAnchor.MiddleLeft, Cyan); t.fontStyle = FontStyle.Bold; SetInset(t.rectTransform, 8f, 0f, 8f, 0f);
        }

        private static RectTransform AddLayoutBlock(string name, float height, Color32 color)
        {
            RectTransform r = MakeRect(name, _content, new Vector2(970f, height)).GetComponent<RectTransform>();
            // Scroll content is top-pivoted; each child must use the same top/stretch geometry so
            // VerticalLayoutGroup owns every row, including the first one, consistently.
            r.anchorMin = new Vector2(0f, 1f); r.anchorMax = new Vector2(1f, 1f); r.pivot = new Vector2(.5f, 1f);
            r.sizeDelta = new Vector2(0f, height);
            LayoutElement le = r.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = height; le.minHeight = height;
            Image image = r.gameObject.AddComponent<Image>(); image.color = color;
            return r;
        }

        private static GameObject MakeRect(string name, Transform parent, Vector2 size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform)); RectTransform r = go.GetComponent<RectTransform>(); r.SetParent(parent, false); r.sizeDelta = size; r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 0f); return go;
        }

        private static RectTransform MakeChild(string name, RectTransform parent, Vector2 size, Vector2 pos)
        {
            RectTransform r = MakeRect(name, parent, size).GetComponent<RectTransform>(); r.anchoredPosition = pos; return r;
        }

        private static Text MakeText(RectTransform parent, string text, int size, TextAnchor anchor, Color color)
        {
            RectTransform r = MakeChild("Text", parent, parent.rect.size, Vector2.zero); r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
            Text t = r.gameObject.AddComponent<Text>(); t.font = Resources.GetBuiltinResource<Font>("Arial.ttf"); t.text = text ?? string.Empty; t.fontSize = size; t.alignment = anchor; t.color = color; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate; t.raycastTarget = false; return t;
        }

        private static Button MakeButton(RectTransform parent, string label, Vector2 size, Vector2 pos, UnityEngine.Events.UnityAction action)
        {
            RectTransform r = MakeChild("Button-" + label, parent, size, pos); Image image = r.gameObject.AddComponent<Image>(); image.color = ButtonColor;
            Button button = r.gameObject.AddComponent<Button>(); if (action != null) button.onClick.AddListener(action);
            ColorBlock colors = button.colors; colors.normalColor = ButtonColor; colors.highlightedColor = new Color32(31, 97, 122, 255); colors.pressedColor = new Color32(8, 171, 219, 255); colors.colorMultiplier = 1f; button.colors = colors;
            MakeText(r, label, 11, TextAnchor.MiddleCenter, Cyan); return button;
        }

        private static InputField MakeInput(RectTransform parent, Vector2 size, Vector2 pos, bool multiline)
        {
            RectTransform r = MakeChild("Input", parent, size, pos); Image image = r.gameObject.AddComponent<Image>(); image.color = FieldColor;
            InputField input = r.gameObject.AddComponent<InputField>(); input.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
            Text text = MakeText(r, string.Empty, 11, TextAnchor.UpperLeft, Color.white); SetInset(text.rectTransform, 7f, 5f, 7f, 5f); text.supportRichText = false;
            Text placeholder = MakeText(r, "Leave empty to use the generated default.", 10, TextAnchor.UpperLeft, Muted); SetInset(placeholder.rectTransform, 7f, 5f, 7f, 5f); placeholder.fontStyle = FontStyle.Italic;
            input.textComponent = text; input.placeholder = placeholder; input.characterLimit = 900; return input;
        }

        private static Dropdown MakeDropdown(RectTransform parent, Vector2 size, Vector2 pos)
        {
            RectTransform r = MakeChild("Dropdown", parent, size, pos);
            r.gameObject.AddComponent<Image>().color = FieldColor;
            Dropdown dd = r.gameObject.AddComponent<Dropdown>();
            Text caption = MakeText(r, string.Empty, 11, TextAnchor.MiddleLeft, Cyan);
            SetInset(caption.rectTransform, 8f, 0f, 30f, 0f);
            dd.captionText = caption;
            Text arrow = MakeText(r, "▼", 11, TextAnchor.MiddleRight, Cyan);
            SetInset(arrow.rectTransform, 0f, 0f, 8f, 0f);

            // Match the geometry contract consumed by UnityEngine.UI.Dropdown.Show(): the template
            // begins immediately below the caption, Content starts at one item high, and Item is the
            // centered stretch-width prototype. Giving Content the full viewport height (and then
            // offsetting a bottom-anchored Item) makes Show's margin calculation place every cloned
            // option outside the masked viewport.
            RectTransform template = MakeChild("Template", r, Vector2.zero, Vector2.zero);
            template.anchorMin = new Vector2(0f, 0f); template.anchorMax = new Vector2(1f, 0f);
            template.pivot = new Vector2(.5f, 1f); template.anchoredPosition = Vector2.zero;
            template.sizeDelta = new Vector2(0f, 180f);
            template.gameObject.AddComponent<Image>().color = PanelColor;
            ScrollRect scroll = template.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false;
            RectTransform viewport = MakeChild("Viewport", template, Vector2.zero, Vector2.zero);
            viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(4f, 4f); viewport.offsetMax = new Vector2(-4f, -4f);
            Image viewportImage = viewport.gameObject.AddComponent<Image>(); viewportImage.color = new Color32(0, 0, 0, 1);
            Mask mask = viewport.gameObject.AddComponent<Mask>(); mask.showMaskGraphic = false;
            RectTransform content = MakeChild("Content", viewport, Vector2.zero, Vector2.zero);
            content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f); content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero; content.sizeDelta = new Vector2(0f, 28f);
            RectTransform item = MakeChild("Item", content, Vector2.zero, Vector2.zero);
            item.anchorMin = new Vector2(0f, .5f); item.anchorMax = new Vector2(1f, .5f); item.pivot = new Vector2(.5f, .5f);
            item.anchoredPosition = Vector2.zero; item.sizeDelta = new Vector2(-4f, 28f);
            Image itemBg = item.gameObject.AddComponent<Image>(); itemBg.color = FieldColor;
            Toggle toggle = item.gameObject.AddComponent<Toggle>(); toggle.targetGraphic = itemBg;
            RectTransform check = MakeChild("Item Checkmark", item, new Vector2(4f, 24f), new Vector2(0f, 2f));
            Image checkImage = check.gameObject.AddComponent<Image>(); checkImage.color = Cyan; toggle.graphic = checkImage;
            Text itemText = MakeText(item, "Option", 11, TextAnchor.MiddleLeft, Cyan); SetInset(itemText.rectTransform, 10f, 0f, 8f, 0f);
            scroll.viewport = viewport; scroll.content = content;
            dd.template = template; dd.itemText = itemText; dd.options = new List<Dropdown.OptionData>();
            template.gameObject.SetActive(false);
            return dd;
        }

        private static Toggle MakeToggle(RectTransform parent, string label, Vector2 size, Vector2 pos)
        {
            RectTransform r = MakeChild("Toggle-" + label, parent, size, pos); Toggle toggle = r.gameObject.AddComponent<Toggle>();
            RectTransform box = MakeChild("Box", r, new Vector2(18f, 18f), new Vector2(0f, 2f)); Image bg = box.gameObject.AddComponent<Image>(); bg.color = FieldColor;
            RectTransform check = MakeChild("Check", box, new Vector2(12f, 12f), new Vector2(3f, 3f)); Image ck = check.gameObject.AddComponent<Image>(); ck.color = Cyan; toggle.targetGraphic = bg; toggle.graphic = ck;
            Text t = MakeText(MakeChild("Label", r, new Vector2(size.x - 22f, size.y), new Vector2(22f, 0f)), label, 9, TextAnchor.MiddleLeft, Muted); t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return toggle;
        }

        private static void SetAnchors(RectTransform r, Vector2 min, Vector2 max, float left, float bottom, float right, float top)
        {
            if (r == null) return;
            r.anchorMin = min; r.anchorMax = max; r.pivot = new Vector2(.5f, .5f);
            r.offsetMin = new Vector2(left, bottom); r.offsetMax = new Vector2(-right, -top);
        }

        private static void SetTopBand(RectTransform r, float topOffset, float height, float left, float right)
        {
            if (r == null) return;
            r.anchorMin = new Vector2(0f, 1f); r.anchorMax = new Vector2(1f, 1f); r.pivot = new Vector2(.5f, 1f);
            r.offsetMin = new Vector2(left, -topOffset - height); r.offsetMax = new Vector2(-right, -topOffset);
        }

        private static void SetBottomBand(RectTransform r, float bottomOffset, float height, float left, float right)
        {
            if (r == null) return;
            r.anchorMin = new Vector2(0f, 0f); r.anchorMax = new Vector2(1f, 0f); r.pivot = new Vector2(.5f, 0f);
            r.offsetMin = new Vector2(left, bottomOffset); r.offsetMax = new Vector2(-right, bottomOffset + height);
        }

        private static void SetInset(RectTransform r, float left, float top, float right, float bottom)
        {
            if (r == null) return; r.offsetMin = new Vector2(left, bottom); r.offsetMax = new Vector2(-right, -top);
        }

        private static string Bound(string value, int max)
        {
            string clean = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim(); return clean.Length <= max ? clean : clean.Substring(0, max - 1) + "…";
        }
    }
    [HarmonyPatch(typeof(CameraController), "UsingUI")]
    internal static class DeepSimsIdentityEditorCameraUsingUiPatch
    {
        [HarmonyPrepare]
        private static bool Prepare()
        {
            try
            {
                MethodInfo method = typeof(CameraController).GetMethod("UsingUI", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                return method != null && method.ReturnType == typeof(bool) && method.GetParameters().Length == 0;
            }
            catch { return false; }
        }

        [HarmonyPostfix]
        private static void Postfix(ref bool __result)
        {
            try { if (!__result && IdentityEditorUi.OwnsCameraInput) __result = true; } catch { }
        }
    }

}
