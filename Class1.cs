using System;
using AutoDisposal;

namespace DeterministicDisposalTest
{

	/// <summary>
	/// Test class that implements IDisposable
	/// </summary>
	public class MyTestDisposableClass : IDisposable
	{
		public virtual void Dispose() { Console.WriteLine("MyTestDisposableClass.Dispose called"); }

	}

	/// <summary>
	/// Test class derived from DeterministicDisposableObject to show how disposal works
	/// </summary>
	public class MyTestDerivedDisposableClass : DeterministicDisposableObject
	{
		public override void Dispose() { Console.WriteLine("MyTestDerivedDisposableClass.Dispose called"); }
	}


	/// <summary>
	/// Test class for DeterministicDisposal. A Contextbound class must be used with our attribute applied to
	/// it in order for deterministic disposal to work correctly.
	/// </summary>
	[DeterministicDisposal()]
	public class TestDeterministicDisposal : ContextBoundObject
	{
		public TestDeterministicDisposal()
		{
		}

		/// <summary>
		/// Test function to simulate some objects that need disposal
		/// </summary>
		public void DoWork()
		{
			//test of a derived deterministic disposable object
			//to be disposed at the end of this function
			MyTestDerivedDisposableClass dobj=new MyTestDerivedDisposableClass();

			//test of protecting a standard disposable object to be cleaned up at the end of this function
			MyTestDisposableClass obj2=new MyTestDisposableClass();
			DeterministicDisposableObject dobj2=new DeterministicDisposableObject(obj2);

		}

	}


	/// <summary>
	/// Summary description for Class1.
	/// </summary>
	class Class1
	{
		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		[STAThread]
		static void Main(string[] args)
		{
			TestDeterministicDisposal tdd=new TestDeterministicDisposal();
			tdd.DoWork();

			// note the console output shows the dispose methods on both variables allocated in
			// tdd.DoWork() were disposed
			
		}
	}
}
