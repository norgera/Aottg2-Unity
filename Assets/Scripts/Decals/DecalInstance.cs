using UnityEngine;

namespace Decals
{
    public class DecalInstance : MonoBehaviour
    {
        public DecalType Type { get; private set; }
        public int OwnerActorNumber { get; private set; }
        public float SpawnTime { get; private set; }
        public float Lifetime { get; private set; }
        public float Size { get; private set; }

        public void Initialize(DecalType type, int ownerActorNumber, float lifetime, float size)
        {
            Type = type;
            OwnerActorNumber = ownerActorNumber;
            Lifetime = lifetime;
            Size = size;
            SpawnTime = Time.time;
        }

        private void Start()
        {
            // Auto-destroy after lifetime expires (if lifetime > 0)
            if (Lifetime > 0f)
            {
                Invoke(nameof(DestroyDecal), Lifetime);
            }
        }

        private void DestroyDecal()
        {
            DecalSpawner.Unregister(this);
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            // Ensure we're unregistered from the active decals list
            DecalSpawner.Unregister(this);
        }
    }
}
