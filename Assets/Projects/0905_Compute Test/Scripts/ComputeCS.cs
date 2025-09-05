using UnityEngine;
using System.Runtime.InteropServices;

public class ComputeCS : MonoBehaviour
{
    public ComputeShader compute;

    ComputeBuffer buffer;
    int NUM_THREADS_SIZE = 4;
    int elementCount = 16;
    int GroupsX(int n) => (n + NUM_THREADS_SIZE - 1) / NUM_THREADS_SIZE;

    //float customNum = 5;

    [StructLayout(LayoutKind.Sequential)]
    struct Test
    {
        public float valueA;
        public float comValue;
        //public float valueB;
        //public float valueC;
    }

    void Start()
    {
        InitBuffer();
        Simulation();
    }

    void OnDestroy()
    {
        buffer?.Release();
    }



    void InitBuffer()
    {
        int stride = Marshal.SizeOf(typeof(Test));
        buffer = new ComputeBuffer(elementCount, stride);
    }

    void Simulation()
    {
        var cs = compute;
        int groups = GroupsX(elementCount);

        int kA = cs.FindKernel("computeA");
        cs.SetInt("_ElementCount", elementCount);
        cs.SetBuffer(kA, "Buffer", buffer);
        cs.Dispatch(kA, groups, 1, 1);

        /*
        int kB = cs.FindKernel("computeB");
        cs.SetInt("_ElementCount", elementCount);
        cs.SetBuffer(kB, "Buffer", buffer);
        cs.Dispatch(kB, groups, 1, 1);

        int kC = cs.FindKernel("computeC");
        cs.SetInt("_ElementCount", elementCount);
        cs.SetFloat("_customNum", customNum);
        cs.SetBuffer(kC, "Buffer", buffer);
        cs.Dispatch(kC, groups, 1, 1);
        */


    }
}
