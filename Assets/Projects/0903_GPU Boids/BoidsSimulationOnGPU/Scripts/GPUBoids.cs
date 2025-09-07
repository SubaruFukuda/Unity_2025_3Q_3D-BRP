using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BoidsSimulationOnGPU
{
    public class GPUBoids : MonoBehaviour
    {
        // Boidデータの構造体
        [System.Serializable]
        struct BoidData
        {
            public Vector3 Velocity; // 速度
            public Vector3 Position; // 位置
        }
        // スレッドグループのスレッドのサイズ
        const int SIMULATION_BLOCK_SIZE = 256;

        #region Boids Parameters
        // 最大オブジェクト数
        [Range(256, 32768 * 2)]
        public int MaxObjectNum = 16384;

        // 結合を適用する他の個体との半径
        public float CohesionNeighborhoodRadius = 2.0f;
        // 整列を適用する他の個体との半径
        public float AlignmentNeighborhoodRadius = 2.0f;
        // 分離を適用する他の個体との半径
        public float SeparateNeighborhoodRadius = 1.0f;

        // 速度の最大値
        public float MaxSpeed = 5.0f;
        // 操舵力の最大値
        public float MaxSteerForce = 0.5f;

        // 結合する力の重み
        public float CohesionWeight = 1.0f;
        // 整列する力の重み
        public float AlignmentWeight = 1.0f;
        // 分離する力の重み
        public float SeparateWeight = 3.0f;

        public string RepelTag = "Repel";
        public float RepelRadius = 3.0f;
        public float RepelWeight = 3.0f;
        [Range(1f, 8f)] public float RepelSharpness = 2.0f; 


        // 壁を避ける力の重み
        public float AvoidWallWeight = 10.0f;

        // 壁の中心座標   
        public Vector3 WallCenter = Vector3.zero;
        // 壁のサイズ
        public Vector3 WallSize = new Vector3(32.0f, 32.0f, 32.0f);
        #endregion

        #region Built-in Resources
        // Boidsシミュレーションを行うComputeShaderの参照
        public ComputeShader BoidsCS;
        #endregion

        #region Private Resources
        // Boidの操舵力（Force）を格納したバッファ
        ComputeBuffer _boidForceBuffer;
        // Boidの基本データ（速度, 位置, Transformなど）を格納したバッファ
        ComputeBuffer _boidDataBuffer;
        //Repelのデータを格納するバッファ
        ComputeBuffer _repelPosBuffer;

        ComputeBuffer _addSpeedBuffer;
        ComputeBuffer _prevMaxSpeed;
        #endregion

        #region GetRepelData
        Transform[] _repelTargets;
        int _repelCount;
        #endregion

        #region Accessors
        // Boidの基本データを格納したバッファを取得
        public ComputeBuffer GetBoidDataBuffer()
        {
            return this._boidDataBuffer != null ? this._boidDataBuffer : null;
        }

        // オブジェクト数を取得
        public int GetMaxObjectNum()
        {
            return this.MaxObjectNum;
        }

        // シミュレーション領域の中心座標を返す
        public Vector3 GetSimulationAreaCenter()
        {
            return this.WallCenter;
        }

        // シミュレーション領域のボックスのサイズを返す
        public Vector3 GetSimulationAreaSize()
        {
            return this.WallSize;
        }
        #endregion

        #region MonoBehaviour Functions
        void Start()
        {
            // バッファを初期化
            InitBuffer();

        }

        void Update()
        {
            // シミュレーション
            UpdateRepelPositions(); // 先に最新位置を送る
            Simulation();           // その後に計算
        }

        void OnDestroy()
        {
            // バッファを破棄
            ReleaseBuffer();
        }

        void OnDrawGizmos()
        {
            // デバッグとしてシミュレーション領域をワイヤーフレームで描画
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(WallCenter, WallSize);
        }
        #endregion

        #region Private Functions
        // バッファを初期化
        void InitBuffer()
        {
            _boidDataBuffer  = new ComputeBuffer(MaxObjectNum, Marshal.SizeOf(typeof(BoidData)));
            _boidForceBuffer = new ComputeBuffer(MaxObjectNum, Marshal.SizeOf(typeof(Vector3)));

            // AddSpeed は float 1要素（★これだけでOK。下の Vector3 で作り直す行は削除）
            _addSpeedBuffer  = new ComputeBuffer(MaxObjectNum, sizeof(float));
            // 前フレームの maxSp を保持
            _prevMaxSpeed    = new ComputeBuffer(MaxObjectNum, sizeof(float));

            // 初期化
            var forceArr    = new Vector3[MaxObjectNum];
            var boidDataArr = new BoidData[MaxObjectNum];
            for (int i = 0; i < MaxObjectNum; i++)
            {
                forceArr[i] = Vector3.zero;
                boidDataArr[i].Position = Random.insideUnitSphere * 1.0f;
                boidDataArr[i].Velocity = Random.insideUnitSphere * 0.1f;
            }
            _boidForceBuffer.SetData(forceArr);
            _boidDataBuffer.SetData(boidDataArr);

            // AddSpeed を 0 で初期化
            var zeros = new float[MaxObjectNum];
            _addSpeedBuffer.SetData(zeros);

            // PrevMaxSpeed を MaxSpeed で初期化
            var initPrev = new float[MaxObjectNum];
            for (int i = 0; i < MaxObjectNum; i++) initPrev[i] = MaxSpeed;
            _prevMaxSpeed.SetData(initPrev);

            // Repel 収集
            var gos = GameObject.FindGameObjectsWithTag(RepelTag);
            _repelTargets = new Transform[gos.Length];
            for (int i = 0; i < gos.Length; i++) _repelTargets[i] = gos[i].transform;
            _repelCount = _repelTargets.Length;

            int count = Mathf.Max(_repelCount, 1);
            _repelPosBuffer = new ComputeBuffer(count, sizeof(float) * 4);
        }

        void UpdateRepelPositions()
        {
            if (_repelPosBuffer == null) return;

            int count = Mathf.Max(_repelCount, 1);
            var temp = new Vector4[count];

            if (_repelCount > 0)
            {
                for (int i = 0; i < _repelCount; i++)
                {
                    var p = _repelTargets[i].position;
                    temp[i] = new Vector4(p.x, p.y, p.z, 1.0f);
                }
            }
            else
            {
                temp[0] = Vector4.zero;
            }

            _repelPosBuffer.SetData(temp);
        }

        // シミュレーション
        void Simulation()
        {
            var cs = BoidsCS;

            // ★ 切り上げは float 割り算で
            int threadGroupSize = Mathf.CeilToInt(MaxObjectNum / (float)SIMULATION_BLOCK_SIZE);

            // ForceCS
            int kF = cs.FindKernel("ForceCS");
            cs.SetInt("_MaxBoidObjectNum", MaxObjectNum);
            cs.SetFloat("_CohesionNeighborhoodRadius", CohesionNeighborhoodRadius);
            cs.SetFloat("_AlignmentNeighborhoodRadius", AlignmentNeighborhoodRadius);
            cs.SetFloat("_SeparateNeighborhoodRadius", SeparateNeighborhoodRadius);
            cs.SetFloat("_MaxSpeed", MaxSpeed);
            cs.SetFloat("_MaxSteerForce", MaxSteerForce);
            cs.SetFloat("_SeparateWeight", SeparateWeight);
            cs.SetFloat("_CohesionWeight", CohesionWeight);
            cs.SetFloat("_AlignmentWeight", AlignmentWeight);
            cs.SetVector("_WallCenter", WallCenter);
            cs.SetVector("_WallSize", WallSize);
            cs.SetFloat("_AvoidWallWeight", AvoidWallWeight);

            cs.SetInt("_RepelCount", _repelCount);
            cs.SetFloat("_RepelRadius", RepelRadius);
            cs.SetFloat("_RepelWeight", RepelWeight);
            cs.SetFloat("_RepelSharpness", RepelSharpness);

            cs.SetBuffer(kF, "_BoidDataBufferRead", _boidDataBuffer);
            cs.SetBuffer(kF, "_BoidForceBufferWrite", _boidForceBuffer);
            cs.SetBuffer(kF, "_RepelPositions", _repelPosBuffer);
            cs.SetBuffer(kF, "_AddSpeedBuffer", _addSpeedBuffer);
            cs.Dispatch(kF, threadGroupSize, 1, 1);

            // IntegrateCS
            int kI = cs.FindKernel("IntegrateCS");
            cs.SetFloat("_DeltaTime", Time.deltaTime);
            // _MaxSpeed を Integrate でも使うならここで更新しておくと安心
            cs.SetFloat("_MaxSpeed", MaxSpeed);

            cs.SetBuffer(kI, "_BoidForceBufferRead", _boidForceBuffer);
            cs.SetBuffer(kI, "_BoidDataBufferWrite", _boidDataBuffer);
            cs.SetBuffer(kI, "_AddSpeedBuffer", _addSpeedBuffer);
            cs.SetBuffer(kI, "_PrevMaxSpeed", _prevMaxSpeed);
            cs.Dispatch(kI, threadGroupSize, 1, 1);
        }


        // バッファを解放
        void ReleaseBuffer()
        {
            if (_boidDataBuffer != null) { _boidDataBuffer.Release(); _boidDataBuffer = null; }
            if (_boidForceBuffer != null) { _boidForceBuffer.Release(); _boidForceBuffer = null; }
            if (_repelPosBuffer != null) { _repelPosBuffer.Release(); _repelPosBuffer = null; }
            if (_addSpeedBuffer != null) { _addSpeedBuffer.Release(); _addSpeedBuffer = null; }
            if (_prevMaxSpeed != null) { _prevMaxSpeed.Release(); _prevMaxSpeed = null; }
        }
    }
        #endregion
}