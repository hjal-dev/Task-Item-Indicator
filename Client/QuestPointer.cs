using System.Collections.Generic;
using System.IO;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.UI;
using UnityEngine;
using UnityEngine.UI;

namespace TaskItemIndicator
{
    public class QuestPointer
    {
        private const float MaxSqrDistance = 20f;
        private const float SqrFullAlphaDistance = 1f;
        private const float SqrMinDistanceForFullPointer = 6.25f;
        private const float MinAlpha = 0.125f;
        private const float AlphaLerpSpeed = 7f;
        private const float ConeCosSq = 0.75f;
        private const int CornerWeightPower = 2;
        private const float InvSqrt2 = 0.70710677f;

        private const float ScanInterval = 1f;

        private static readonly string[] CornerNames = { "UpLeftQuestItemPointer", "UpRightQuestItemPointer", "DownLeftQuestItemPointer", "DownRightQuestItemPointer" };
        private static readonly Vector2[] CornerPositions = { new Vector2(-5f, 5f), new Vector2(5f, 5f), new Vector2(-5f, -5f), new Vector2(5f, -5f) };
        private static readonly float[] CornerRotations = { 90f, 0f, 180f, 270f };
        private static readonly Vector2[] CornerDirections = { new Vector2(-InvSqrt2, InvSqrt2), new Vector2(InvSqrt2, InvSqrt2), new Vector2(-InvSqrt2, -InvSqrt2), new Vector2(InvSqrt2, -InvSqrt2) };

        private static Sprite _sprite;

        private readonly Image[] _corners = new Image[4];
        private readonly float[] _alphas = new float[4];
        private readonly float[] _targets = new float[4];
        private readonly List<LootItem> _questLoot = new List<LootItem>();

        private GameWorld _world;
        private ActionPanel _panel;
        private Camera _camera;
        private float _nextScan;

        public void Tick()
        {
            GameWorld world = Singleton<GameWorld>.Instance;
            Player player = world != null ? world.MainPlayer : null;
            if (player == null)
            {
                Remove();
                return;
            }

            if (world != _world)
            {
                Remove();
                _world = world;
            }

            if (Time.time >= _nextScan)
            {
                _nextScan = Time.time + ScanInterval;
                ScanQuestLoot(world, player);
                if (_panel == null)
                {
                    CreateCorners();
                }
                if (_camera == null)
                {
                    _camera = Camera.main;
                }
            }

            if (_panel == null || _corners[0] == null || !_panel.gameObject.activeInHierarchy || _camera == null)
            {
                return;
            }

            LootItem target = FindNearestQuestLoot(player.Position);
            if (target != null)
            {
                CalculateTargets(player.Position, target.transform.position);
            }
            else
            {
                _targets[0] = _targets[1] = _targets[2] = _targets[3] = 0f;
            }

            float step = Time.deltaTime * AlphaLerpSpeed;
            for (int i = 0; i < 4; i++)
            {
                _alphas[i] = Mathf.Lerp(_alphas[i], _targets[i], step);
                _corners[i].color = new Color(1f, 1f, 1f, _alphas[i]);
            }
        }

        public void Remove()
        {
            for (int i = 0; i < 4; i++)
            {
                if (_corners[i] != null)
                {
                    Object.Destroy(_corners[i].gameObject);
                }
                _corners[i] = null;
                _alphas[i] = 0f;
            }

            _panel = null;
            _camera = null;
            _world = null;
            _questLoot.Clear();
            _nextScan = 0f;
            _loggedCount = -1;
        }

        private void ScanQuestLoot(GameWorld world, Player player)
        {
            _questLoot.Clear();
            MongoID profileId = player.ProfileId;
            bool wantedBuilt = false;

            List<IKillable> loot = world.LootList;
            for (int i = 0; i < loot.Count; i++)
            {
                LootItem item = loot[i] as LootItem;
                if (item == null || item.Item == null)
                {
                    continue;
                }

                MongoID[] valid = item.ValidProfiles;
                if (valid != null)
                {
                    for (int j = 0; j < valid.Length; j++)
                    {
                        if (valid[j] == profileId)
                        {
                            _questLoot.Add(item);
                            break;
                        }
                    }
                    continue;
                }

                if (!item.Item.QuestItem)
                {
                    continue;
                }

                if (!wantedBuilt)
                {
                    BuildWantedTemplates(player);
                    wantedBuilt = true;
                }

                if (_wantedTemplates.Contains(item.Item.StringTemplateId))
                {
                    _questLoot.Add(item);
                }
            }

            if (_questLoot.Count != _loggedCount)
            {
                _loggedCount = _questLoot.Count;
                Log?.Invoke("task items in this raid: " + _loggedCount);
            }
        }

        private readonly HashSet<string> _wantedTemplates = new HashSet<string>();
        private readonly HashSet<string> _carriedTemplates = new HashSet<string>();
        private int _loggedCount = -1;

        private void BuildWantedTemplates(Player player)
        {
            _wantedTemplates.Clear();
            _carriedTemplates.Clear();

            foreach (Item carried in player.Profile.Inventory.GetPlayerItems(EPlayerItems.QuestItems))
            {
                _carriedTemplates.Add(carried.StringTemplateId);
            }

            QuestController quests = player.QuestController;
            if (quests == null || quests.Quests == null)
            {
                return;
            }

            foreach (Quest quest in quests.Quests)
            {
                if (quest.QuestStatus != EQuestStatus.Started)
                {
                    continue;
                }

                foreach (ConditionFindItem condition in quest.GetConditions<ConditionFindItem>(EQuestStatus.AvailableForFinish))
                {
                    if (quest.CompletedConditions.Contains(condition.id)
                        || (quest.ProgressCheckers.TryGetValue(condition, out ConditionProgressChecker checker) && checker.Test()))
                    {
                        continue;
                    }

                    foreach (string template in condition.target)
                    {
                        if (!_carriedTemplates.Contains(template))
                        {
                            _wantedTemplates.Add(template);
                        }
                    }
                }
            }
        }

        private LootItem FindNearestQuestLoot(Vector3 playerPosition)
        {
            LootItem nearest = null;
            float best = MaxSqrDistance;

            for (int i = 0; i < _questLoot.Count; i++)
            {
                LootItem item = _questLoot[i];
                if (item == null)
                {
                    continue;
                }

                float sqr = (item.transform.position - playerPosition).sqrMagnitude;
                if (sqr < best)
                {
                    best = sqr;
                    nearest = item;
                }
            }

            return nearest;
        }

        private void CalculateTargets(Vector3 playerPosition, Vector3 lootPosition)
        {
            float sqrDistance = (lootPosition - playerPosition).sqrMagnitude;

            float distanceAlpha = sqrDistance <= SqrFullAlphaDistance
                ? 1f
                : Mathf.Clamp01(1f - (sqrDistance - SqrFullAlphaDistance) / (MaxSqrDistance - SqrFullAlphaDistance));

            Transform view = _camera.transform;
            Vector3 toLoot = lootPosition - view.position;

            float x = Vector3.Dot(toLoot, view.right);
            float y = Vector3.Dot(toLoot, view.up);
            float z = Vector3.Dot(toLoot, view.forward);

            bool insideLookCone = z > 0f && z * z >= ConeCosSq * toLoot.sqrMagnitude;
            bool fullPointer = insideLookCone && sqrDistance < SqrMinDistanceForFullPointer;

            float length = Mathf.Sqrt(x * x + y * y);
            if (fullPointer || length < 1e-6f)
            {
                for (int i = 0; i < 4; i++)
                {
                    _targets[i] = Mathf.Max(MinAlpha, distanceAlpha);
                }
                return;
            }

            Vector2 direction = new Vector2(x / length, y / length);

            float strongest = 0f;
            for (int i = 0; i < 4; i++)
            {
                float u = (1f + Vector2.Dot(direction, CornerDirections[i])) * 0.5f;
                float weight = u;
                for (int p = 1; p < CornerWeightPower; p++)
                {
                    weight *= u;
                }
                _targets[i] = weight;
                strongest = Mathf.Max(strongest, weight);
            }

            for (int i = 0; i < 4; i++)
            {
                _targets[i] = Mathf.Max(MinAlpha, _targets[i] / strongest * distanceAlpha);
            }
        }

        private void CreateCorners()
        {
            ActionPanel panel = Object.FindObjectOfType<ActionPanel>();
            if (panel == null || panel._pointer == null)
            {
                return;
            }

            Sprite sprite = LoadSprite();
            if (sprite == null)
            {
                return;
            }

            int siblingIndex = panel._pointer.transform.GetSiblingIndex() + 1;

            for (int i = 0; i < 4; i++)
            {
                GameObject corner = new GameObject(CornerNames[i], typeof(RectTransform));
                corner.layer = panel.gameObject.layer;

                RectTransform rect = (RectTransform)corner.transform;
                rect.SetParent(panel.transform, false);
                rect.SetSiblingIndex(siblingIndex + i);
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(20f, 20f);
                rect.anchoredPosition = CornerPositions[i];
                rect.localEulerAngles = new Vector3(0f, 0f, CornerRotations[i]);
                rect.localScale = Vector3.one;

                Image image = corner.AddComponent<Image>();
                image.sprite = sprite;
                image.type = Image.Type.Simple;
                image.preserveAspect = false;
                image.raycastTarget = false;
                image.color = new Color(1f, 1f, 1f, 0f);

                _corners[i] = image;
                _alphas[i] = 0f;
            }

            _panel = panel;

            Canvas canvas = panel.GetComponentInParent<Canvas>();
            Log?.Invoke("attached to " + panel.transform.parent.name + "/" + panel.name + ", canvas scale " + (canvas != null ? canvas.rootCanvas.scaleFactor.ToString("0.###") : "?")
                + " at " + Screen.width + "x" + Screen.height + ", task items for this raid: " + _questLoot.Count);
        }

        public System.Action<string> Log;

        private static Sprite LoadSprite()
        {
            if (_sprite != null)
            {
                return _sprite;
            }

            using (Stream stream = typeof(QuestPointer).Assembly.GetManifestResourceStream("TaskItemIndicator.Semicircle_Sprite.png"))
            {
                if (stream == null)
                {
                    return null;
                }

                byte[] bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);

                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
                texture.LoadImage(bytes, true);
                texture.filterMode = FilterMode.Bilinear;
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.name = "Semicircle_Sprite";

                _sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                _sprite.name = "Semicircle_Sprite";
                Object.DontDestroyOnLoad(texture);
                return _sprite;
            }
        }
    }
}
