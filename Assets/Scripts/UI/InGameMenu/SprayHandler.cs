using System.Collections.Generic;
using UnityEngine;
using Settings;
using GameManagers;
using ApplicationManagers;
using Utility;
using Decals;
using System.Text.RegularExpressions;

namespace UI
{
    class SprayHandler : MonoBehaviour
    {
        private BasePopup _sprayWheelPopup;
        private InGameManager _inGameManager;
        private float _sprayCooldownLeft;
        private const float SprayCooldown = 5f;
        private const float MaxSprayDistance = 30f;
        private readonly List<string> _builtinSprays = new List<string>();
        private readonly List<string> _sprayPageBuffer = new List<string>();
        private int _pageIndex = 0; // 7 options per page + Custom
        public bool IsActive;

        private void Awake()
        {
            _sprayWheelPopup = ElementFactory.InstantiateAndSetupPanel<WheelPopup>(transform, "Prefabs/InGame/WheelMenu").GetComponent<BasePopup>();
            _inGameManager = (InGameManager)SceneLoader.CurrentGameManager;
            // Defer loading until first open to avoid loading overhead during gameplay/death
        }

        private static int CompareKeys(string a, string b)
        {
            // Strip BG: prefix
            string na = a.StartsWith("BG:") ? a.Substring(3) : a;
            string nb = b.StartsWith("BG:") ? b.Substring(3) : b;
            // Try to find the first number in each name
            var ma = Regex.Match(na, @"(\d+)");
            var mb = Regex.Match(nb, @"(\d+)");
            if (ma.Success && mb.Success)
            {
                int ia = int.Parse(ma.Value);
                int ib = int.Parse(mb.Value);
                int cmp = ia.CompareTo(ib);
                if (cmp != 0) return cmp;
            }
            return string.Compare(na, nb, System.StringComparison.OrdinalIgnoreCase);
        }
        private void LoadSpraySlots()
        {
            // Populate with all backgrounds under Resources/UI/Backgrounds
            _builtinSprays.Clear();
            // Load Texture2D backgrounds
            var backgrounds = Resources.LoadAll<Texture2D>("UI/Backgrounds");
            foreach (var tex in backgrounds)
            {
                if (tex == null) continue;
                // Store by name so subfolder assets resolve too (DecalSpawner resolves by name)
                string key = $"BG:{tex.name}";
                if (!_builtinSprays.Contains(key))
                    _builtinSprays.Add(key);
            }
            // Load Sprites as well (UI textures often are Sprites)
            var bgSprites = Resources.LoadAll<Sprite>("UI/Backgrounds");
            foreach (var spr in bgSprites)
            {
                if (spr == null) continue;
                string key = $"BG:{spr.name}";
                if (!_builtinSprays.Contains(key))
                    _builtinSprays.Add(key);
            }
            // Remove blood-themed and dark backgrounds from the wheel
            _builtinSprays.RemoveAll(s => s.StartsWith("BG:") && (s.ToLower().Contains("blood") || s.ToLower().Contains("darkbackground")));
            // Natural sort by numeric suffix if present (e.g., MainBackground0Texture, MainBackground1Texture, ...)
            _builtinSprays.Sort(CompareKeys);
            _pageIndex = 0;
            // Keep a few simple emoji options at the end if desired (optional)
            //_builtinSprays.Add("UI/Emotes/EmojiSmile");
            //_builtinSprays.Add("UI/Emotes/EmojiThumbsUp");
            //_builtinSprays.Add("UI/Emotes/EmojiCool");
        }

        public void SetCustomSprayUrl(string url)
        {
            // This method is deprecated - custom spray is now handled through skin settings
            // Left for backward compatibility but does nothing
        }

        public void ToggleSprayWheel()
        {
            SetSprayWheel(!_sprayWheelPopup.gameObject.activeSelf);
        }

        public void SetSprayWheel(bool enable)
        {
            if (enable)
            {
                if (!InGameMenu.InMenu())
                {
                    if (_builtinSprays.Count == 0)
                        LoadSpraySlots();
                    var options = GetSprayWheelOptions();
                    if (options.Count == 0)
                    {
                        // Fallback: ensure at least one option so the wheel doesn't index out of range
                        options.Add("Custom");
                    }
                    // Wheel supports up to 8 buttons
                    if (options.Count > 8)
                        options = options.GetRange(0, 8);
                    IsActive = true;
                    ((WheelPopup)_sprayWheelPopup).Show(SettingsManager.InputSettings.Interaction.ItemMenu.ToString(), options, () => OnSprayWheelSelect());
                }
            }
            else
            {
                _sprayWheelPopup.Hide();
                IsActive = false;
            }
        }

        private List<string> GetSprayWheelOptions()
        {
            _sprayPageBuffer.Clear();
            int start = _pageIndex * 7;
            int count = Mathf.Min(7, Mathf.Max(0, _builtinSprays.Count - start));
            for (int i = 0; i < count; i++)
            {
                string s = _builtinSprays[start + i];
                string name = s.StartsWith("BG:") ? s.Substring(3) : s;
                _sprayPageBuffer.Add(name);
            }
            if (_pageIndex == 0)
                _sprayPageBuffer.Add("Custom");
            return new List<string>(_sprayPageBuffer);
        }

        private void Update()
        {
            _sprayCooldownLeft -= Time.deltaTime;
        }

        public void NextSprayWheel()
        {
            int totalPages = Mathf.Max(1, (_builtinSprays.Count + 6) / 7);
            _pageIndex = (_pageIndex + 1) % totalPages;
            var options = GetSprayWheelOptions();
            if (options.Count > 8)
                options = options.GetRange(0, 8);
            ((WheelPopup)_sprayWheelPopup).Show(SettingsManager.InputSettings.Interaction.ItemMenu.ToString(), options, () => OnSprayWheelSelect());
        }

        private void OnSprayWheelSelect()
        {
            if (!SettingsManager.GraphicsSettings.SpraysEnabled.Value)
            {
                _sprayWheelPopup.Hide();
                IsActive = false;
                return;
            }
            if (_sprayCooldownLeft > 0f)
            {
                _sprayWheelPopup.Hide();
                IsActive = false;
                return;
            }

            var character = _inGameManager.CurrentCharacter;
            if (character == null || !character.IsMine())
            {
                _sprayWheelPopup.Hide();
                IsActive = false;
                return;
            }

            int selected = ((WheelPopup)_sprayWheelPopup).SelectedItem;
            if (selected < 0) selected = 0;
            int indexOnPage = selected;
            int globalIndex = _pageIndex * 7 + indexOnPage;
            bool isCustom = indexOnPage >= _sprayPageBuffer.Count - 1 || globalIndex >= _builtinSprays.Count;
            
            string idOrUrl;
            if (isCustom)
            {
                // Get custom spray from current human skin setting
                var humanSkinSettings = SettingsManager.CustomSkinSettings.Human;
                var selectedSet = (HumanCustomSkinSet)humanSkinSettings.GetSelectedSet();
                idOrUrl = selectedSet.CustomSpray.Value;
            }
            else
            {
                idOrUrl = _builtinSprays[globalIndex];
            }
            
            if (string.IsNullOrEmpty(idOrUrl))
            {
                _sprayWheelPopup.Hide();
                IsActive = false;
                CursorManager.SetHidden(true);
                return;
            }

            // Raycast to place spray
            var camera = SceneLoader.CurrentCamera;
            Ray ray = camera.Camera.ScreenPointToRay(CursorManager.GetInGameMousePosition());
            if (Physics.Raycast(ray, out RaycastHit hit, MaxSprayDistance, PhysicsLayer.GetMask(PhysicsLayer.MapObjectAll, PhysicsLayer.MapObjectEntities)))
            {
                Vector3 position = hit.point;
                Vector3 normal = hit.normal;
                // Compute surface-up by projecting the camera's up onto the surface plane to keep the image visually upright
                Vector3 camUp = SceneLoader.CurrentCamera.Cache.Transform.up;
                Vector3 surfaceUp = (camUp - Vector3.Dot(camUp, normal) * normal).normalized;
                float size = 5f; // adjustable upper bound
                float lifetime = 30f; // fade out

                // Use SolidSpray for custom sprays to prevent bleeding, regular Spray for default ones
                DecalType sprayType = isCustom ? DecalType.SolidSpray : DecalType.Spray;
                DecalSpawner.SpawnNetworkedOriented(sprayType, idOrUrl, position, normal, surfaceUp, size, lifetime, replaceExisting: true);
                _sprayCooldownLeft = SprayCooldown;
            }

            _sprayWheelPopup.Hide();
            IsActive = false;
            ((InGameMenu)UIManager.CurrentMenu).SkipAHSSInput = true;
        }
    }
}


