﻿#region .NET/MONO System Libraries
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Globalization;
using System.Threading.Tasks;
#endregion

#region Much appreciated third-party Libraries
using MathNet.Numerics.Distributions;
using MathNet.Numerics.Statistics;
#endregion

#region NETGen Libraries
using NETGen.Core;
using NETGen.NetworkModels.Cluster;
using NETGen.Visualization;
using NETGen.Dynamics.Synchronization;
using NETGen.pyspg;
#endregion

public class ClusterSync : NETGen.pyspg.pyspgSimulation<ClusterSync>
{	
	[Parameter(ParameterType.Input, "Number of nodes", 500)]
	int nodes;
	
	[Parameter(ParameterType.Input, "When to consider the network synchronized", 0.95d)]
	double orderThres;
	
	[Parameter(ParameterType.Input, "coupling strength", 10d)]
	double K;
	
	[Parameter(ParameterType.Input, "Maximum time to simulate", 100d)]
	double timeThres;
	
	[Parameter(ParameterType.Input, "Number of edges", 4000)]
	int edges;
	
	[Parameter(ParameterType.Input, "Number of clusters", 20)]
	int clusters;
	
	[Parameter(ParameterType.Input, "Probability for pacemakers to emerge", 0)]
	double pacemakerProb;
	
	[Parameter(ParameterType.Input, "Desired Newman modularity of the network", 0)]
	double modularity_tgt;
	
	[Parameter(ParameterType.Input, "Mean of cluster mean frequencies", 1d )]
	double global_mean;
		
	[Parameter(ParameterType.Input, "Width factor for cluster mean frequency distribution", 1d/5d)]
	double global_mean_width_factor;
		
	[Parameter(ParameterType.Input, "StdDev of node frequencies in cluster", 1d/5d)]
	double cluster_width_factor;
	
	[Parameter(ParameterType.OutputFile, "Evolution of cluster order and global order")]
	string dynamics;
	
#pragma warning disable 0414
	[Parameter(ParameterType.Output, "Final order")]
	double finalOrderParam;
	
	[Parameter(ParameterType.Output, "integrated order, normalized by simulation time")]
	double normalizerIntegratedOrder;
	
	[Parameter(ParameterType.Output, "Initial Density")]
	double initialDensity;
	
	[Parameter(ParameterType.Output, "Final density")]
	double finalDensity;
	
	[Parameter(ParameterType.Output, "Measured Newman modularity of the network")]
	double modularity_real;
	
	[Parameter(ParameterType.Output, "Time taken to synchronize")]
	double time;	
#pragma warning restore 0414	
	
	public static void Main(string[] args)
    {	
		Init(args);
	}
	
	/// <summary>
	/// Runs the cluster synchronization experiments, reading simulation parameters from the .config file
	/// </summary>
    public override void RunSimulation ()
	{
		// Setup the experiment by creating the network and the synchronization module
        ClusterNetwork net = new ClusterNetwork(nodes, edges, clusters, modularity_tgt, true);
    	Kuramoto sync = new Kuramoto(net, K);
		
		// Couple to single random neighbor in each step
		sync.CouplingSelector = new Func<Vertex, Vertex[]>( v => {
			return new Vertex[] {v.RandomNeighbor};
		});
		
		// Keeps track whether clusters have already switched to pacemaker mode
		Dictionary<int,bool> pacemaker_mode = new Dictionary<int, bool>();
		
		// Mixed distribution of natural frequencies
		Normal group_avgs = new Normal( global_mean, global_mean * global_mean_width_factor );
		foreach(int i in net.ClusterIDs)
		{
			double group_avg = group_avgs.Sample();
			Normal group_dist = new Normal( group_avg, group_avg * cluster_width_factor );
			foreach(Vertex v in net.GetNodesInCluster(i))
				sync.NaturalFrequencies[v] = group_dist.Sample();			
			pacemaker_mode[i] = false;
		}					    	
    	
    	// Set up handler that will be called AFTER each simulation step
    	sync.OnStep+= new Kuramoto.StepHandler(
    	delegate(double t) 
		{
			// Compute global order parameter
    		finalOrderParam = sync.GetOrder(net.Vertices.ToArray());	
			
			normalizerIntegratedOrder += finalOrderParam;
			
			// Record order evolution
			sync.AddDataPoint("GlobalOrder", finalOrderParam);
			
			// Stop simulation if full ordered state is reached or time exceeded
    		if (finalOrderParam>= orderThres || t >timeThres)
    			sync.Stop(); 
			
			// Output will only been shown when pyspg module is started in debug mode
    		//if(t % (sync.TimeDelta * 100d) == 0)
    			Logger.AddMessage(LogEntryType.SimMsg, string.Format("Time {0}, Order = {1:0.00}", t, finalOrderParam));
			
			// Compute order parameter of individual clusters
			foreach(int g in net.ClusterIDs)
			{
				double localOrder = sync.GetOrder(net.GetNodesInCluster(g));				
				sync.AddDataPoint(string.Format("ClusterOrder_{0}", g), localOrder);				
				
				// Switch to pacemaker mode if cluster order exceeds threshold
				if(localOrder>orderThres && !pacemaker_mode[g])
				{
					pacemaker_mode[g] = true;
					
					// Probabilistically switch border nodes to pacemaker mode
					// Note: CouplingStrengths[... v, w ... ] is the strength by which phase advance of v is influenced when coupling to node w
					foreach(Vertex v in net.GetNodesInCluster(g))
					{
						if(net.HasInterClusterConnection(v))
							foreach(Vertex w in v.Neigbors)
								if(!net.HasInterClusterConnection(w) && net.NextRandomDouble() <= pacemakerProb)
									{
										Logger.AddMessage(LogEntryType.AppMsg, string.Format("Vertex switched to pacemaker mode", g));
										sync.CouplingStrengths[new Tuple<Vertex, Vertex>(v, w)] = 0d;
									}
					}
				}
			}
    	});
		
		// compute coupling density in the initial situation
		initialDensity = 0d;
		foreach(var t in sync.CouplingStrengths.Keys)
			initialDensity += sync.CouplingStrengths[t];		
    	
    	// Synchronously run the experiment (blocks execution until experiment is finished)
    	sync.Run();
		
		// compute final coupling density
		finalDensity = 0d;		
		foreach(var t in sync.CouplingStrengths.Keys)
			finalDensity += sync.CouplingStrengths[t];
		
		// Write time-series of the order parameters (global and cluster-wise) to a file
    	sync.WriteTimeSeries(dynamics);
		
		// Set results     	
		normalizerIntegratedOrder /= sync.Time;
	    time = sync.Time;
		finalOrderParam = sync.GetOrder(net.Vertices.ToArray());
	    modularity_real = net.NewmanModularity;
	}
}