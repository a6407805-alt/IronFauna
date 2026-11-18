using UnityEngine;

namespace IronFauna.Boxing
{
    /// <summary>
    /// Sparring partner: circles at the edge of range, steps in to throw short combinations, steps back out,
    /// reads the player's wind-ups (guard or slip after a human-ish reaction delay) and covers up when hurt.
    /// </summary>
    [RequireComponent(typeof(MechBoxer))]
    public class MechBoxerAI : MonoBehaviour
    {
        [Range(0f, 1f)] public float skill = 0.55f;
        public float idealRange = 2.7f;
        /// <summary>Keep this far inside the centre (half-size of the floor); &lt;= 0 uses the boxer's ring size.</summary>
        public float edgeHalfSize = -1f;

        private MechBoxer _me;
        private float _circleDir = 1f;
        private float _circleT;
        private float _thinkT;
        private float _guardT;
        private float _reactT = -1f;
        private bool _reacting;
        private readonly System.Collections.Generic.Queue<PunchType> _combo = new System.Collections.Generic.Queue<PunchType>();
        private float _comboGap;
        private enum Mode { Circle, Engage, Retreat }
        private Mode _mode = Mode.Circle;
        private float _modeT;

        private static readonly PunchType[][] Combos =
        {
            new[] { PunchType.Jab },
            new[] { PunchType.Jab, PunchType.Jab },
            new[] { PunchType.Jab, PunchType.Cross },
            new[] { PunchType.Jab, PunchType.Cross, PunchType.Hook },
            new[] { PunchType.Cross, PunchType.Hook },
            new[] { PunchType.Hook, PunchType.Uppercut },
            new[] { PunchType.Jab, PunchType.Uppercut, PunchType.Hook },
        };

        private void Awake() => _me = GetComponent<MechBoxer>();

        private void Update()
        {
            var opp = _me.opponent;
            if (opp == null || _me.IsKO || opp.IsKO)
            {
                _me.intent.move = Vector2.zero;
                _me.intent.guard = false;
                return;
            }
            float dt = Time.deltaTime;
            Vector3 d = opp.transform.position - transform.position;
            d.y = 0f;
            float dist = d.magnitude;

            // ---------------- defence: see the wind-up, react after a delay
            if (opp.Telegraphing && !_reacting)
            {
                _reacting = true;
                _reactT = Mathf.Lerp(0.26f, 0.1f, skill) + Random.Range(0f, 0.08f);
            }
            if (!opp.Punching) _reacting = false;
            if (_reactT > 0f)
            {
                _reactT -= dt;
                if (_reactT <= 0f && opp.Punching && !_me.Punching)
                {
                    float r = Random.value;
                    if (r < skill * 0.45f) _guardT = 0.5f;
                    else if (r < skill * 0.7f) _me.intent.slip = Random.value < 0.5f ? -1 : 1;
                }
            }
            float hurt = 1f - _me.health / _me.maxHealth;
            _guardT -= dt;

            // ---------------- footwork modes
            _modeT -= dt;
            _circleT -= dt;
            if (_circleT <= 0f) { _circleDir = Random.value < 0.5f ? -1f : 1f; _circleT = Random.Range(0.8f, 2.2f); }
            if (_modeT <= 0f)
            {
                float r = Random.value;
                _mode = r < 0.45f + skill * 0.15f ? Mode.Engage : (r < 0.8f ? Mode.Circle : Mode.Retreat);
                _modeT = _mode == Mode.Engage ? Random.Range(0.9f, 1.6f) : Random.Range(0.6f, 1.4f);
                if (_mode == Mode.Engage) QueueCombo();
            }

            Vector2 move = Vector2.zero;
            switch (_mode)
            {
                case Mode.Circle:
                    move.x = _circleDir * 0.8f;
                    move.y = Mathf.Clamp((dist - (idealRange + 0.6f)) * 1.2f, -1f, 1f);
                    break;
                case Mode.Engage:
                    move.x = _circleDir * 0.25f;
                    move.y = Mathf.Clamp((dist - (idealRange - 0.25f)) * 1.6f, -1f, 1f);
                    break;
                case Mode.Retreat:
                    move.x = _circleDir * 0.6f;
                    move.y = -0.9f;
                    break;
            }
            // keep off the ropes
            Vector3 p = transform.position;
            float edge = edgeHalfSize > 0f ? edgeHalfSize : _me.ringHalfSize - 1.2f;
            if (Mathf.Abs(p.x) > edge || Mathf.Abs(p.z) > edge)
            {
                Vector3 toCentre = transform.InverseTransformDirection(-new Vector3(p.x, 0f, p.z).normalized);
                move += new Vector2(toCentre.x, toCentre.z) * 0.8f;
            }
            _me.intent.move = Vector2.ClampMagnitude(move, 1f);

            // ---------------- offence
            _comboGap -= dt;
            if (_combo.Count > 0 && dist < idealRange + 0.35f && _comboGap <= 0f && !_me.Punching && _guardT <= 0f)
            {
                _me.intent.punch = _combo.Dequeue();
                _comboGap = Random.Range(0.08f, 0.2f);
                if (_combo.Count == 0) { _mode = Mode.Retreat; _modeT = Random.Range(0.5f, 0.9f); }
            }
            // counter right after the player whiffs or finishes
            _thinkT -= dt;
            if (_thinkT <= 0f)
            {
                _thinkT = 0.25f;
                if (!opp.Punching && dist < idealRange + 0.2f && _combo.Count == 0 && Random.value < 0.18f + skill * 0.2f) QueueCombo();
            }

            // hurt: covers up more often (sticky, so the guard does not flicker)
            if (hurt > 0.5f && !_me.Punching && _combo.Count == 0 && _guardT <= 0f && Random.value < hurt * 0.6f * dt) _guardT = Random.Range(0.5f, 1.1f);
            _me.intent.guard = _guardT > 0f;
        }

        private void QueueCombo()
        {
            _combo.Clear();
            var c = Combos[Random.Range(0, Combos.Length)];
            foreach (var p in c) _combo.Enqueue(p);
        }
    }
}
