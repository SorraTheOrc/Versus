using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using static WizardsCode.Versus.Controllers.CityController;
using WizardsCode.Versus.Controllers;
using static WizardsCode.Versus.Controller.AnimalController;
using System.Text;
using NeoFPS;
using WizardsCode.Versus.FPS;

namespace WizardsCode.Versus.Controller
{
    public class BlockController : MonoBehaviour
    {
        public enum Priority { Low, Medium, High};

        [Header("Meta Data")]
        [HideInInspector, SerializeField, Tooltip("The x,y coordinates of this block within the city.")]
        public Vector2Int Coordinates;
        [SerializeField, Tooltip("Block size in units.")]
        internal Vector2 m_Size = new Vector2(100, 100);
        [HideInInspector, SerializeField, Tooltip("The type of block this is. The block type dictates what is generated within the block.")]
        public BlockType BlockType;

        [SerializeField, Tooltip("The number of excess faction members that need to be present for dominance. This is used to calculate a factions influence on the block. If a faction has a majority of this many on a block then it is considered to have dominance.")]
        int m_FactionMembersNeededForControl = 5;

        [Header("UX")]
        [SerializeField, Tooltip("The mesh that will show the faction control.")]
        Transform m_FactionMap;
        [SerializeField, Tooltip("The frequency, in seconds, the faction map should be updated. This is a costly operation on large maps so don't make it too frequent.")]
        float m_FactionMapUpdateFrequency = 1f;

        [SerializeField, Tooltip("READ ONLY: This text field will be updated with a description of the blocks status in play mode.")]
        string m_DebugInfo = "Available in Play Mode only.";

        private List<AnimalController> m_DogsPresent = new List<AnimalController>();
        private List<AnimalController> m_CatsPresent = new List<AnimalController>();
        Priority m_CatPriority = Priority.Medium;
        Priority m_DogPriority = Priority.Medium;

        SpawnPoint m_FpsSpawnPoint;
        private Mesh m_FactionMesh;

        public delegate void OnBlockUpdatedDelegate(BlockController block, VersuseEvent versusEvent);
        public OnBlockUpdatedDelegate OnBlockUpdated;

        private float timeOfNextFactionMapUpdate = 0;
        private Faction previousFaction;

        public CityController CityController { get; private set; }
        /// <summary>
        /// Returns the number of faction members needed to ensure dominance int his block.
        /// To have dominance the faction must have this many more members present than any other
        /// faction.
        /// </summary>
        public int FactionMembersForDominance
        {
            get { return m_FactionMembersNeededForControl; }
        }
        /// <summary>
        /// Get a list of all the Dats that currently consider this block their home.
        /// </summary>
        public List<AnimalController> Cats
        {
            get { return m_CatsPresent; }
        }
        /// <summary>
        /// Get a list of all the Dogs that currently consider this block their home.
        /// </summary>
        public List<AnimalController> Dogs
        {
            get { return m_DogsPresent; }
        }

        PlayerCharacter player = null;

        public Faction ControllingFaction
        {
            get
            {
                if (NormalizedFactionInfluence <= 0.1f)
                {
                    return Faction.Cat;
                }
                else if (NormalizedFactionInfluence >= 0.9f)
                {
                    return Faction.Dog;
                } else
                {
                    return Faction.Neutral;
                }
            }
        }

        private void Start()
        {
            m_FactionMesh = m_FactionMap.GetComponent<MeshFilter>().mesh;
            CityController = FindObjectOfType<CityController>();
        }

        internal void SetPriority(Faction faction, Priority priority)
        {
            if (faction == Faction.Cat)
            {
                m_CatPriority = priority;
            } else
            {
                m_DogPriority = priority;
            }
        }

        internal SpawnPoint GetFpsSpawnPoint()
        {
            if (m_FpsSpawnPoint == null)
            {
                m_FpsSpawnPoint = transform.GetComponentInChildren<SpawnPoint>();
            }
            return m_FpsSpawnPoint;
        }

        private void OnTriggerEnter(Collider other)
        {
            AnimalController animal = other.GetComponentInParent<AnimalController>();
            if (animal && animal.currentState != State.Attack)
            {
                animal.HomeBlock.RemoveAnimal(animal);
                AddAnimal(animal);
                return;
            }

            PlayerCharacter character = other.GetComponentInChildren<PlayerCharacter>();
            if (character)
            {
                character.CurrentBlock = this;
                player = character;
            }
        }
        private void OnTriggerExit(Collider other)
        {
            PlayerCharacter character = other.GetComponentInChildren<PlayerCharacter>();
            if (character)
            {
                player = null;
            }
        }

        internal void AddAnimal(AnimalController animal)
        {
            animal.transform.SetParent(transform);
            animal.HomeBlock = this;

            switch (animal.m_Faction) {
                case AnimalController.Faction.Cat:
                    m_CatsPresent.Add(animal);
                    break;
                case AnimalController.Faction.Dog:
                    m_DogsPresent.Add(animal);
                    break;
            }

            OnBlockUpdated(this, new BlockUpdateEvent($"{animal.m_Faction} moved into {ToString()}."));
        }

        internal void RemoveAnimal(AnimalController animal)
        {
            switch (animal.m_Faction)
            {
                case AnimalController.Faction.Cat:
                    m_CatsPresent.Remove(animal);
                    break;
                case AnimalController.Faction.Dog:
                    m_DogsPresent.Remove(animal);
                    break;
            }

            OnBlockUpdated(this, new BlockUpdateEvent($"{animal.m_Faction} moved out of {ToString()}."));
        }

        /// <summary>
        /// Gets a random point within this block that an animal might want to go to.
        /// </summary>
        /// <returns></returns>
        internal Vector3 GetRandomPoint()
        {
            //TODO: more intelligent spawning location, currently animals can spawn on top of one another, inside buildings and more.
            return transform.position +  new Vector3(Random.Range(-m_Size.x / 2, m_Size.x / 2), 0, Random.Range(-m_Size.y / 2, m_Size.y / 2));
        }

        private void Update()
        {
            if (Time.timeSinceLevelLoad >= timeOfNextFactionMapUpdate) {
                timeOfNextFactionMapUpdate = m_FactionMapUpdateFrequency + Time.timeSinceLevelLoad;
                UpdateFactionInfluence();
            }
            UpdateAnimalAI();

# if UNITY_EDITOR
            UpdateDebugInfo();
#endif
        }

        private void UpdateAnimalAI()
        {
            if (player)
            {
                for (int i = 0; i < m_DogsPresent.Count; i++)
                {
                    m_DogsPresent[i].currentState = State.Attack;
                    m_DogsPresent[i].target = player.transform;
                }
            }
        }

        private void UpdateFactionInfluence()
        {
            Vector3[] vertices = m_FactionMesh.vertices;
            Color32[] colors = new Color32[vertices.Length];

            Color32 blockColor = CityController.m_FactionGradient.Evaluate(NormalizedFactionInfluence);
            for (int i = 0; i < vertices.Length; i++)
            {
                colors[i] = blockColor;
            }

            m_FactionMesh.colors32 = colors;

            if (ControllingFaction != previousFaction)
            {
                switch (ControllingFaction)
                {
                    case Faction.Cat:
                        OnBlockUpdated(this, new BlockUpdateEvent($"The cats have taken {ToString()}.", Importance.High));
                        break;
                    case Faction.Dog:
                        OnBlockUpdated(this, new BlockUpdateEvent($"The dogs have taken {ToString()}.", Importance.High));
                        break;
                    case Faction.Neutral:
                        if (previousFaction == Faction.Cat)
                        {
                            OnBlockUpdated(this, new BlockUpdateEvent($"The dogs have weakened the cats hold on {ToString()}, it is now a neutral zone: (Normalized Influence: {NormalizedFactionInfluence}).", Importance.High));
                        }
                        else
                        {
                            OnBlockUpdated(this, new BlockUpdateEvent($"The cats have weakened the dogs hold on {ToString()}, it is now a neutral zone (Normalized Influence: {NormalizedFactionInfluence}).", Importance.High));
                        }
                        break;
                }
                previousFaction = ControllingFaction;
            }
        }

        /// <summary>
        /// Get a normalized value that represents the influence of each faction on this block.
        /// 0.5 is neutral, 0 is cat controlled, 1 is dog controlled
        /// </summary>
        /// <returns>0.5 is neutral, 0 is cat controlled, 1 is dog controlled</returns>
        internal float NormalizedFactionInfluence
        {
            get
            {
                float m_CurrentInfluence = 0.5f;
                if (m_DogsPresent.Count == m_CatsPresent.Count)
                {
                    m_CurrentInfluence = 0.5f;
                } else if (m_DogsPresent.Count > m_CatsPresent.Count)
                {
                    float influence = (float)(m_DogsPresent.Count - m_CatsPresent.Count) / m_FactionMembersNeededForControl;
                    m_CurrentInfluence = Mathf.Clamp01(0.5f + (influence / 2));
                }
                else
                {
                    float influence = (float)(m_CatsPresent.Count - m_DogsPresent.Count) / m_FactionMembersNeededForControl;
                    m_CurrentInfluence = Mathf.Clamp01(0.5f - (influence / 2));
                }

                return m_CurrentInfluence;
            }
        }

        public override string ToString()
        {
            return $"{name} {Coordinates}.";
        }

        private void UpdateDebugInfo()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Block Influence (0 to 1,  cats to dogs): {NormalizedFactionInfluence}");
            sb.AppendLine();
            sb.AppendLine($"There are {m_CatsPresent.Count} Cats present.");
            sb.AppendLine($"The Cat director has set a priorty of {m_CatPriority} on this block.");
            sb.AppendLine();
            sb.AppendLine($"There are {m_DogsPresent.Count} Dogs present.");
            sb.AppendLine($"The Dog director has set a priorty of {m_DogPriority} on this block.");

            m_DebugInfo = sb.ToString();
        }
    }
}
