/* Written by David Risack (c) 2004.
 * You may use this work as long as you leave the copyright intact
 * 
 * 
 */

using System;
using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Proxies;
using System.Runtime.Remoting.Contexts;
using System.Runtime.Remoting.Activation;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Services;

namespace AutoDisposal
{
	/// <summary>
	/// Used to store constant for internal name used for call context properties or other deterministic disposal 
	/// constants
	/// </summary>
	internal class DeterministicConstants
	{
		internal const string DETERMINISTIC_DISPOSAL_PROPERTY_NAME="__DeterministicDisposal";

	}

	/// <summary>
	/// This proxy class is used to be interjected between a contextbound object so that we can
	/// intercept the method calls to track disposable items and dispose of the object when completed
	/// </summary>
	internal class DeterministicDisposalProxy : RealProxy
	{
		readonly MarshalByRefObject target;	// used to store the target of the proxy

		/// <summary>
		/// Constructor of the proxy used to instatiate with the target object and type
		/// </summary>
		/// <param name="target"></param>
		/// <param name="type"></param>
		public DeterministicDisposalProxy(MarshalByRefObject target, Type type)
			: base(type)
		{
			this.target=target;

			//Get our stack object from the call context if it is there
			object obj=CallContext.GetData(DeterministicConstants.DETERMINISTIC_DISPOSAL_PROPERTY_NAME);

			if (null==obj)
			{
				//if the stack object is not already in the call context create it
				CallContext.SetData(DeterministicConstants.DETERMINISTIC_DISPOSAL_PROPERTY_NAME,new Stack());
			}
		}

		/// <summary>
		/// The Invoke method will be called for each function call on our contextbound object where our attribute was applied
		/// </summary>
		/// <param name="request">Information about the method request</param>
		/// <returns></returns>
		public override IMessage Invoke(IMessage request)
		{
			IMessage response = null;
			IMethodCallMessage call = (IMethodCallMessage)request;
			IConstructionCallMessage ctor= call as IConstructionCallMessage;

			//Get the stack object that was created in our constructor for the proxy
			//So we can get the current high watermark of the stack so we know the objects
			//to dispose of after the method call returns
			object obj=CallContext.GetData(DeterministicConstants.DETERMINISTIC_DISPOSAL_PROPERTY_NAME);
			Stack DisposalStack=(Stack)obj;

			//stack was not created if this fails
			Debug.Assert(obj!=null);

			//Get our current high watermark for the stack
			int BeforeCallStackTop=DisposalStack.Count;

			//see if this is the constructor call
			if (ctor!=null) 
			{
				//We need to create and return our proxy here to return it instead of the actual contextbound object
				//itself
				RealProxy defaultProxy = RemotingServices.GetRealProxy(target);
				defaultProxy.InitializeServerObject(ctor);
				MarshalByRefObject tp = (MarshalByRefObject)this.GetTransparentProxy();
				response = EnterpriseServicesHelper.CreateConstructionReturnMessage(ctor,tp);
			} 
			else 
			{
				//Execute the method call
				response = RemotingServices.ExecuteMessage(target, call);
			}

			//Get the stacks current high water mark so we know how many items to pop off for disposal
			int ItemsToPopOffStack=DisposalStack.Count-BeforeCallStackTop;
			Debug.Assert(ItemsToPopOffStack>=0);

			//If there were new items added that need to be disposed of then
			if (ItemsToPopOffStack>0)
			{
				//Loop to pop all of the new items off the stack for Deterministic Disposal
				for (int i=0; i<ItemsToPopOffStack; i++)
				{
					IDeterministicDispose id=(IDeterministicDispose)DisposalStack.Pop();
					try
					{
						//Call our interface which in turn will call the standard IDisposable interface
						id.DeterministicDispose();
					}
					catch
					{
						Trace.WriteLine("DeterministicDisposalProxy exception - object may have already been finalized or destroyed");
						Debug.Assert(false); //check to see if this actually happens remove for production code
					}
				}

			}
				
			return response;
			
		}

		
	}

	/// <summary>
	/// DeterministicDisposalAttribute is the attribute that must be applied to contextbound derived classes
	/// in order to take advantage of using the DeterministicDisposableObject base class (or as an object)
	/// This attribute causes our proxy to be injected between the contextbound object and callers
	/// </summary>
	[AttributeUsage(AttributeTargets.Class)]
	internal class DeterministicDisposalAttribute : ProxyAttribute //, IContextAttribute
	{
		
		public DeterministicDisposalAttribute()
		{
		}

		/// <summary>
		/// CreateInstance used to create our real proxy and return it to the caller
		/// </summary>
		/// <param name="t"></param>
		/// <returns></returns>
		public override MarshalByRefObject CreateInstance(Type t)
		{
			MarshalByRefObject target=base.CreateInstance(t);
			RealProxy pp= (RealProxy)new DeterministicDisposalProxy(target, t);
			return (MarshalByRefObject)pp.GetTransparentProxy();
		}
		

		/// <summary>
		/// IsContextOK just indicates to use the same context for this new object by returning true
		/// </summary>
		/// <param name="ctx"></param>
		/// <param name="msg"></param>
		/// <returns></returns>
		new public bool IsContextOK(Context ctx, IConstructionCallMessage msg)
		{
			return true;
		}

		
	}

	/// <summary>
	/// IDeterministicDispose our internal interface to be called for deterministic disposal
	/// </summary>
	internal interface IDeterministicDispose
	{
		void DeterministicDispose();
	}

	/// <summary>
	/// DeterministicDisposableObject this object can be used as a base class or just to pass an object to protect
	/// by passing the object to the constructor.  Should be used within a method only. 
	/// The class that uses this in a method should have been derived from ContextBoundObject for it
	/// to be deterministically disposed.  Should not be used in a non-contextbound object as call context would be saved 
	/// and would not be cleaned up, plus the objects would never get destroyed
	/// </summary>
	public class DeterministicDisposableObject : IDisposable, IDeterministicDispose
	{
		/// <summary>
		/// Default constructor puts our object in the callcontext stack so we will get disposed when the method call is done
		/// </summary>
		public DeterministicDisposableObject()
		{
			Stack DisposableStack=(Stack)CallContext.GetData(DeterministicConstants.DETERMINISTIC_DISPOSAL_PROPERTY_NAME);
			Debug.Assert(DisposableStack!=null);
			DisposableStack.Push(this);
		}

		/// <summary>
		/// Constructor used to pass an object that is IDisposable
		/// </summary>
		/// <param name="o"></param>
		public DeterministicDisposableObject(IDisposable o)
		{
			//Save reference to our object so we can call it's dispose later
			ReferencedObject_=o;

			Stack DisposableStack=(Stack)CallContext.GetData(DeterministicConstants.DETERMINISTIC_DISPOSAL_PROPERTY_NAME);
			Debug.Assert(DisposableStack!=null);
			DisposableStack.Push(this);
		}
		
		/// <summary>
		/// virtual Dispose function can be override by a derived class to implement objects own dispose
		/// </summary>
		public virtual void Dispose() { }


		/// <summary>
		/// internal DeterministicDispose dispose function calls dispose on referenced object, this object
		/// and frees interop handles to allow for garbage collection
		/// </summary>
		public void DeterministicDispose()
		{
			if (null!=ReferencedObject_)
			{
				try
				{
					ReferencedObject_.Dispose();
				}
				catch
				{
					Trace.WriteLine("DisposableObject.DeterministicDispose Exception while calling passed objects Dispose.");
				}

			}

			try
			{
				//call our virtual member function.  It may have been overridden so we should protect
				//from failure
				Dispose();
			}
			catch
			{
				Trace.WriteLine("DisposableObject.DeterministicDispose Exception while calling passed objects Dispose.");
			}

		}

		//Class members
		private IDisposable ReferencedObject_;	//used for passed in object
		
	}


}
