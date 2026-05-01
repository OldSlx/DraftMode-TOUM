using System.Text;
using HarmonyLib;
using MiraAPI.LocalSettings;
using TMPro;
using TownOfUs.Patches;
using UnityEngine;
using DraftModeTOUM.Patches;

namespace DraftModeTOUM.Managers
{
    public static class DraftSidebarManager
    {
        private static bool _active = false;
        private static readonly string ColPlayerName  = "#ffdd00ff";
        private static readonly string ColLocalPlayer = "#8bd5f9ff";

        // ── Banner ────────────────────────────────────────────────────────────
        private static GameObject    _bannerGo;
        private static SpriteRenderer _bannerSr;

        // ── Activate / Deactivate ─────────────────────────────────────────────

        public static void Activate()
        {
            if (!IsSettingEnabled()) return;
            _active = true;
            EnsureBanner();
            if (_bannerGo != null) _bannerGo.SetActive(true);
            DraftModePlugin.Logger.LogInfo("[DraftSidebar] Activated.");
        }

        public static void Deactivate()
        {
            if (!_active) return;
            _active = false;

            if (_bannerGo != null) _bannerGo.SetActive(false);

            // Clear the text we wrote so TOU-Mira's UpdateRoleList reclaims the panel
            // on its next tick and shows the normal role/neutral list again.
            var tmp = HudManagerPatches.RoleListTextComp;
            if (tmp != null)
                tmp.text = string.Empty;

            // Also hide the panel so TOU-Mira can restore it properly on next Update tick.
            var roleList = HudManagerPatches.RoleList;
            if (roleList != null)
                roleList.SetActive(false);

            // Make sure IsHoveringRoleList is not stuck true — that would prevent
            // TOU-Mira's UpdateRoleList from writing new text.
            HudManagerPatches.IsHoveringRoleList = false;

            DraftModePlugin.Logger.LogInfo("[DraftSidebar] Deactivated.");
        }

        /// <summary>
        /// Nulls out the cached banner references (call on disconnect / scene change
        /// so EnsureBanner() re-parents to the new HUD on the next draw).
        /// </summary>
        public static void ClearBannerRef()
        {
            _bannerGo = null;
            _bannerSr = null;
        }

        public static bool IsActive => _active;

        // ── Draw ──────────────────────────────────────────────────────────────

        public static void DrawSidebar()
        {
            var roleList = HudManagerPatches.RoleList;
            var tmp      = HudManagerPatches.RoleListTextComp;
            if (roleList == null || tmp == null) return;

            // Lazy-init the banner in case RoleList wasn't ready at Activate() time.
            EnsureBanner();
            if (_bannerGo != null && !_bannerGo.activeSelf)
                _bannerGo.SetActive(true);

            roleList.SetActive(true);
            tmp.fontSize    = 3f;
            tmp.fontSizeMin = 0.5f;
            tmp.fontSizeMax = 3f;
            tmp.text        = BuildText();
        }

        // ── Banner helpers ────────────────────────────────────────────────────

        private static void EnsureBanner()
        {
            // Reuse if already valid.
            if (_bannerGo != null) return;

            var roleList = HudManagerPatches.RoleList;
            if (roleList == null) return;

            _bannerGo = new GameObject("DraftSidebarBanner");
            _bannerGo.transform.SetParent(roleList.transform, false);

            _bannerSr                  = _bannerGo.AddComponent<SpriteRenderer>();
            _bannerSr.sortingLayerName = "UI";
            _bannerSr.sortingOrder     = 51;

            var sprite = DraftAssets.DraftBanner.LoadAsset();
            if (sprite != null)
            {
                _bannerSr.sprite = sprite;

                // DraftBanner.png is loaded at PPU 50 (see DraftAssets.cs).
                // Rendered size in Unity units = texturePx / 50.
                // Adjust localScale and localPosition once you see it in-game;
                // these defaults place it flush above the first text line.
                _bannerGo.transform.localScale    = Vector3.one * 0.38f;
                _bannerGo.transform.localPosition = new Vector3(1.55001f, -1.0001f, -1f);
            }
            else
            {
                DraftModePlugin.Logger.LogWarning("[DraftSidebar] DraftBanner sprite failed to load.");
            }

            // Start hidden; Activate() / DrawSidebar() will enable it.
            _bannerGo.SetActive(false);
        }

        // ── Settings check ────────────────────────────────────────────────────

        private static bool IsSettingEnabled()
        {
            var settings = LocalSettingsTabSingleton<DraftModeLocalSettings>.Instance;
            return settings != null && settings.ShowDraftSidebar.Value;
        }

        // ── Text building ─────────────────────────────────────────────────────

        private static string BuildText()
        {
            var sb = new StringBuilder();

            // Blank first line acts as a vertical spacer below the banner image.
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine();


            if (!DraftManager.IsDraftActive)
            {
                sb.Append($"<color=#ffffffff>Waiting...</color>");
                return sb.ToString();
            }

            foreach (int slot in DraftManager.TurnOrder)
            {
                var state = DraftManager.GetStateForSlot(slot);
                if (state == null) continue;

                bool   isMe     = state.PlayerId == PlayerControl.LocalPlayer.PlayerId;
                string nameCol  = isMe ? ColLocalPlayer : ColPlayerName;

                sb.AppendLine(
                    $"<color={nameCol}><b>Player {slot:D2}</b></color> " +
                    BuildStatusLine(state));
            }

            return sb.ToString().TrimEnd();
        }

        private static string BuildStatusLine(PlayerDraftState state)
        {
            if (state.IsDisconnected)
                return $"<color=#ffffffff>DISCONNECTED</color>";

            if (state.IsPickingNow && !state.HasPicked)
                return $"<color=#ffffffff>is picking...</color>";

            if (state.HasPicked && state.ChosenRoleId.HasValue)
            {
                var faction = GetFactionForRole(state.ChosenRoleId.Value);
                switch (faction)
                {
                    case RoleFaction.Impostor:
                        return $"has picked <color=#FF4444><b>IMPOSTOR</b></color>";
                    case RoleFaction.NeutralKilling:
                    case RoleFaction.Neutral:
                        return $"has picked <color=#7e7e7eff>NEUTRAL</color>";
                    default:
                        return $"has picked <color=#00FFFF>CREWMATE</color>";
                }
            }

            if (state.HasPicked)
                return $"has picked <color=#00FFFF>CREWMATE</color>";

            return $"<color=#ffffffff>is waiting for turn</color>";
        }

        private static RoleFaction GetFactionForRole(ushort roleId)
        {
            try
            {
                var role = RoleManager.Instance?.GetRole((AmongUs.GameOptions.RoleTypes)roleId);
                if (role != null) return RoleCategory.GetFactionFromRole(role);
            }
            catch { }
            return RoleFaction.Crewmate;
        }
    }

    // ── Harmony hooks ─────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(HudManagerPatches), nameof(HudManagerPatches.UpdateRoleList))]
    public static class DraftSidebarUpdateRoleListPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (!DraftSidebarManager.IsActive) return;
            DraftSidebarManager.DrawSidebar();
        }
    }

    [HarmonyPatch(typeof(DraftNetworkHelper), nameof(DraftNetworkHelper.BroadcastDraftStart))]
    public static class DraftSidebarActivateOnStart
    {
        [HarmonyPostfix]
        public static void Postfix() => DraftSidebarManager.Activate();
    }

    [HarmonyPatch(typeof(DraftManager), nameof(DraftManager.SetDraftStateFromHost))]
    public static class DraftSidebarActivateOnClient
    {
        [HarmonyPostfix]
        public static void Postfix() => DraftSidebarManager.Activate();
    }

    [HarmonyPatch(typeof(DraftNetworkHelper), nameof(DraftNetworkHelper.BroadcastRecap))]
    public static class DraftSidebarDeactivateOnRecap
    {
        [HarmonyPostfix]
        public static void Postfix() => DraftSidebarManager.Deactivate();
    }

    [HarmonyPatch(typeof(DraftNetworkHelper), nameof(DraftNetworkHelper.BroadcastCancelDraft))]
    public static class DraftSidebarDeactivateOnCancel
    {
        [HarmonyPostfix]
        public static void Postfix() => DraftSidebarManager.Deactivate();
    }

    /// <summary>
    /// Catches all draft-end paths on both host and client:
    /// - Normal completion → SetState(Hidden) via CoEndDraftSequence
    /// - Cancel → already caught above, but this is a universal safety net
    /// Every exit path transitions the overlay to Hidden, so this is reliable.
    /// </summary>
    [HarmonyPatch(typeof(DraftStatusOverlay), nameof(DraftStatusOverlay.SetState))]
    public static class DraftSidebarDeactivateOnOverlayHidden
    {
        [HarmonyPostfix]
        public static void Postfix(OverlayState state)
        {
            if (state == OverlayState.Hidden)
                DraftSidebarManager.Deactivate();
        }
    }

    /// <summary>
    /// Belt-and-suspenders: deactivate when the game actually starts
    /// so the sidebar never leaks into in-game.
    /// Also clears the banner ref so it re-parents correctly on the next lobby.
    /// </summary>
    [HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.CoBegin))]
    public static class DraftSidebarDeactivateOnIntro
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            DraftSidebarManager.Deactivate();
            DraftSidebarManager.ClearBannerRef();
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnDisconnected))]
    public static class DraftSidebarDeactivateOnDisconnect
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            DraftSidebarManager.Deactivate();
            DraftSidebarManager.ClearBannerRef();
        }
    }

    // ── RoleListHoverComponent suppression ────────────────────────────────────
    //
    // RoleListHoverComponent lives on HudManager.Instance.gameObject and drives
    // the "hover the role list panel to show bucket tooltips" feature.
    // It sets HudManagerPatches.IsHoveringRoleList = true while the mouse is
    // over the panel, which blocks UpdateRoleList from writing new text
    // (HudManagerPatches.UpdateRoleList: "if (!IsHoveringRoleList) text = ...").
    //
    // During draft we own the role list panel for the sidebar, so we patch
    // RoleListHoverComponent.Update: when a draft is active the original is
    // skipped and IsHoveringRoleList is forced to false, ensuring our
    // DrawSidebar() postfix is never blocked.
    //
    // OnEnable is NOT patched here — it is inherited from MonoBehaviour and
    // Harmony cannot patch inherited methods via the subtype.

    [HarmonyPatch(typeof(RoleListHoverComponent), nameof(RoleListHoverComponent.Update))]
    public static class RoleListHoverSuppressUpdate
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            if (!DraftManager.IsDraftActive) return true; // run normally outside draft

            // Draft is active: prevent hover detection and clear any stale flag so
            // our DrawSidebar postfix is never blocked by UpdateRoleList's guard.
            HudManagerPatches.IsHoveringRoleList = false;
            return false; // skip original Update
        }
    }
}