using UnityEngine;

namespace FreeFire
{
    public class LootItem : MonoBehaviour
    {
        [SerializeField] private ItemData    itemData;
        [SerializeField] private int         count       = 1;
        [SerializeField] private float       interactDist = 2.5f;
        [SerializeField] private Transform   label;
        [SerializeField] private Animator    floatAnim;

        private bool _pickedUp;

        private void Awake()
        {
            if (floatAnim != null) floatAnim.enabled = true;
        }

        private void OnEnable() => _pickedUp = false;

        private void Start()
        {
            if (label != null) label.gameObject.SetActive(true);
        }

        private void Update()
        {
            // Billboard label toward camera
            if (label != null && Camera.main != null)
                label.LookAt(label.position + Camera.main.transform.rotation * Vector3.forward,
                             Camera.main.transform.rotation * Vector3.up);
        }

        public bool TryPickup(InventorySystem inventory)
        {
            if (_pickedUp || inventory == null) return false;
            if (!inventory.TryAdd(itemData, count)) return false;

            _pickedUp = true;
            AudioManager.Instance?.PlayPositional("pickup_generic", transform.position, 0.7f);
            gameObject.SetActive(false);
            return true;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, interactDist);
        }
    }
}