using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;

public class Boid : MonoBehaviour
{
    public Simulation simulation { get; set; }
    public Param param { get; set; }
    public Vector3 pos { get; private set; }
    public Vector3 velocity { get; private set; }
    Vector3 accel = Vector3.zero;
    List<Boid> neighbors = new List<Boid>();
    public float posSmooth = 0.5f;

    private float noise1;
    private float noise2;
    private float rand1;
    private float rand2;
    private float mapped1;
    private float mapped2;

    private string repelTag = "Repel";

    public float timespeed = 0.1f;
    private float time;

    void Start()
    {
        pos = transform.position;
        velocity = transform.forward * param.initSpeed;

        rand1 = UnityEngine.Random.Range(-1000, 1000); //noiseのシード値設定
    }

    void Update()
    {
        // 近隣の個体を探して neighbors リストを更新
        UpdateNeighbors();

        // 壁に当たりそうになったら向きを変える
        UpdateWalls();

        // 近隣の個体から離れる
        UpdateSeparation();

        // 近隣の個体と速度を合わせる
        UpdateAlignment();

        // 近隣の個体の中心に移動する
        UpdateCohesion();

        // 特定のタグのオブジェクトを避ける
        UpdateRepel();

        // 上記 4 つの結果更新された accel を velocity に反映して位置を動かす
        UpdateMove();

        time += Time.deltaTime * timespeed;

        //SeparateValue
        noise1 = Unity.Mathematics.noise.snoise(new float2(rand1, time));
        mapped1 = Mathf.Lerp(0.5f, 3f, (noise1 + 1f) / 2f);
        
        //AlignmentValue
        noise2 = Unity.Mathematics.noise.snoise(new float2(rand2, time));
        mapped2 = Mathf.Lerp(0.8f, 3.5f, (noise2 + 1f) / 2f);

        //posにスムージング
    }

    void UpdateMove()
    {
        var dt = Time.deltaTime;

        velocity += accel * dt;
        var dir = velocity.normalized;
        var speed = velocity.magnitude;
        velocity = Mathf.Clamp(speed, param.minSpeed, param.maxSpeed) * dir;
        pos += velocity * dt; //positionの変数

        var rot = Quaternion.LookRotation(velocity); //rotationの変数
        transform.SetPositionAndRotation(pos, rot);
        accel = Vector3.zero;
    }

    void UpdateWalls()
    {
        if (!simulation) return;

        var scale = param.wallScale * 0.5f;
        accel +=
            CalcAccelAgainstWall(-scale - pos.x, Vector3.right) +
            CalcAccelAgainstWall(-scale - pos.y, Vector3.up) +
            CalcAccelAgainstWall(-scale - pos.z, Vector3.forward) +
            CalcAccelAgainstWall(+scale - pos.x, Vector3.left) +
            CalcAccelAgainstWall(+scale - pos.y, Vector3.down) +
            CalcAccelAgainstWall(+scale - pos.z, Vector3.back);
    }

    Vector3 CalcAccelAgainstWall(float distance, Vector3 dir)
    {
        if (distance < param.wallDistance)
        {
            return dir * (param.wallWeight / Mathf.Abs(distance / param.wallDistance));
        }
        return Vector3.zero;
    }

    void UpdateNeighbors()
    {
        neighbors.Clear();

        if (!simulation) return;

        var prodThresh = Mathf.Cos(param.neighborFov * Mathf.Deg2Rad);
        var distThresh = param.neighborDistance;

        foreach (var other in simulation.boids)
        {
            if (other == this) continue;

            var to = other.pos - pos;
            var dist = to.magnitude;
            if (dist < distThresh)
            {
                var dir = to.normalized;
                var fwd = velocity.normalized;
                var prod = Vector3.Dot(fwd, dir);
                if (prod > prodThresh)
                {
                    neighbors.Add(other);
                }
            }
        }
    }

    void UpdateSeparation()
    {
        if (neighbors.Count == 0) return;

        Vector3 force = Vector3.zero;
        foreach (var neighbor in neighbors)
        {
            force += (pos - neighbor.pos).normalized;
        }
        force /= neighbors.Count;

        accel += force * (param.separationWeight);
    }

    void UpdateAlignment()
    {
        if (neighbors.Count == 0) return;

        var averageVelocity = Vector3.zero;
        foreach (var neighbor in neighbors)
        {
            averageVelocity += neighbor.velocity;
        }
        averageVelocity /= neighbors.Count;

        accel += (averageVelocity - velocity) * (param.alignmentWeight);
    }

    void UpdateCohesion()
    {
        if (neighbors.Count == 0) return;

        var averagePos = Vector3.zero;
        foreach (var neighbor in neighbors)
        {
            averagePos += neighbor.pos;
        }
        averagePos /= neighbors.Count;

        accel += (averagePos - pos) * param.cohesionWeight;
    }

  void UpdateRepel()
    {
        // パラメータ未設定なら何もしない
        if (param == null || param.repelRadius <= 0f || param.repelWeight <= 0f) return;

        float radius = param.repelRadius;

        // 半径内の当たりを取得
        var hits = Physics.OverlapSphere(pos, radius);
        if (hits == null || hits.Length == 0) return;

        Vector3 repelForce = Vector3.zero;
        int count = 0;

        foreach (var hit in hits)
        {
            if (hit == null) continue;

            // タグ一致のみ有効
            if (!hit.CompareTag(repelTag)) continue;

            // コライダーの最接近点を使うと安定
            Vector3 nearest = hit.ClosestPoint(pos);
            Vector3 away = pos - nearest;
            float dist = away.magnitude;
            if (dist <= 1e-4f) continue;

            // 距離が近いほど強く、遠いほど弱く（0〜1）
            float t = 1f - Mathf.Clamp01(dist / radius);
            // 二乗で立ち上がりを滑らかに
            float falloff = t * t;

            repelForce += away.normalized * falloff;
            count++;
        }

        if (count > 0)
        {
            // 重みを掛けて加速度に加算
            accel += repelForce * param.repelWeight;
        }
    }
}