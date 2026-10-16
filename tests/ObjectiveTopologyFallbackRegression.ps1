$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$d = Get-Content (Join-Path $root 'RKmission/MissionDungeon.cs') -Raw
$start = $d.IndexOf('        private void ResetObjectiveApproach()')
$end = $d.IndexOf('        private bool HoldForReadiness()', $start)
$methods = $d.Substring($start, $end - $start)
$fieldsStart = $d.IndexOf('        private readonly List<Vector3> _objectiveApproaches')
$fieldsEnd = $d.IndexOf('        private float _objectiveBestDistance', $fieldsStart)
$fields = $d.Substring($fieldsStart, $fieldsEnd - $fieldsStart)
$code = @'
using System;
using System.Collections.Generic;
using System.Linq;
public struct Vector3 {
 public float X,Y,Z;
 public Vector3(float x,float y,float z){X=x;Y=y;Z=z;}
 public static float Distance(Vector3 a,Vector3 b) => (float)Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2)+Math.Pow(a.Z-b.Z,2));
}
public class Room { public int Instance = 10; }
public class Player { public Vector3 Position; }
public static class DynelManager { public static Player LocalPlayer = new Player(); }
public enum MovementOwner { Objective }
public static class SMovementController { public static bool Navigating; public static bool IsNavigating()=>Navigating; }
public class MovementArbiter {
 public static MovementArbiter Current = new MovementArbiter();
 public int DirectCalls;
 public bool SetNavDestination(MovementOwner owner,Vector3 point){DirectCalls++; SMovementController.Navigating=true; return true;}
 public void Halt(MovementOwner owner){SMovementController.Navigating=false;}
}
public static class DungeonApproachPlanner {
 public static List<Vector3> Points = new List<Vector3>();
 public static IEnumerable<Vector3> Candidates(object layout,int room,Vector3 from,Vector3 target,float[] radii,float tolerance)=>Points;
}
public class NavigationRouteRecorder {
 public int Prepared, Driven, Stopped;
 public bool Available=true, Usable=true, Complete=false;
 public bool TryPrepareDungeonTopology(object layout,int room,Vector3 from,Vector3 target,out string name){Prepared++;name="ramp";return Available;}
 public bool TryNavigatePreparedDungeonTopology(Vector3 target,MovementArbiter movement,MovementOwner owner,out bool completed){Driven++;completed=Complete;return Usable;}
 public void StopPlayback(){Stopped++;}
}
public class Harness {
 private object _layout = new object();
 private Vector3? _destination;
 private MovementOwner _requestedOwner = MovementOwner.Objective;
 private NavigationRouteRecorder _navRoutes = new NavigationRouteRecorder();
 private float _objectiveBestDistance;
 private DateTime _objectiveProgressAt;
 private Action<string> _say = _=>{};
'@ + $fields + $methods + @'
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static Vector3 Target = new Vector3(78.8f,13.005f,238.01f);
 static Harness New(){DynelManager.LocalPlayer.Position=new Vector3(78,5,238.1f);DungeonApproachPlanner.Points.Clear();SMovementController.Navigating=false;return new Harness();}
 public static void Run(){
  var room=new Room(); var h=New();
  DungeonApproachPlanner.Points.Add(new Vector3(80,13,238));
  Check(h.NavigateObjective(room,Target),"Direct approach should navigate");
  Check(h._navRoutes.Prepared==0 && h._destination.HasValue,"Direct route must precede topology");
  h=New(); Check(h.NavigateObjective(room,Target),"Prepare fallback");
  Check(h._navRoutes.Prepared==1 && h._objectiveTopologyActive,"Vertical fallback prepared");
  Check(h.NavigateObjective(room,Target) && h._navRoutes.Driven==1,"Drive ordered playback");
  h._navRoutes.Complete=true;
  DynelManager.LocalPlayer.Position=new Vector3(78,13.005f,238);
  Check(h.NavigateObjective(room,Target),"Completion must yield interaction recheck even with no final mesh");
  Check(!h._objectiveTopologyActive && h._objectiveApproaches.Count==0,"Completion clears active topology");
  Check(!h.NavigateObjective(room,Target) && h._navRoutes.Prepared==1,"No second topology attempt after completion");
  h=New();h.NavigateObjective(room,Target);h._navRoutes.Usable=false;
  Check(!h.NavigateObjective(room,Target) && h._navRoutes.Stopped==1,"Playback refusal cleans up");
  Check(!h.NavigateObjective(room,Target) && h._navRoutes.Prepared==1,"Failed route cannot restart");
  h=New();h.NavigateObjective(room,Target);h._objectiveTopologyStarted=DateTime.UtcNow.AddSeconds(-61);
  Check(!h.NavigateObjective(room,Target) && h._navRoutes.Driven==0,"Bounded timeout before further playback");
  h=New();h._navRoutes.Available=false;
  Check(!h.NavigateObjective(room,Target),"No recording fails cleanly");
  Check(!h.NavigateObjective(room,Target) && h._navRoutes.Prepared==1,"Unavailable recording cannot loop");
  h=New();h.NavigateObjective(room,Target);h.ResetObjectiveApproach();
  Check(!h.NavigateObjective(room,Target) && h._navRoutes.Prepared==1,"Interruption preserves attempt bound");
  h=New();Check(!h.NavigateObjective(room,new Vector3(78,5.5f,238)),"Flat blocked approach fails");
  Check(h._navRoutes.Prepared==0,"Flat targets must not select topology");
  h=New();h.NavigateObjective(room,Target);h._navRoutes.Complete=true;
  DynelManager.LocalPlayer.Position=new Vector3(78,13,238);
  DungeonApproachPlanner.Points.Add(new Vector3(79,13,238));
  h.NavigateObjective(room,Target);
  Check(h._objectiveApproaches.Count==1,"Final approaches rebuilt at upper level");
  h.NavigateObjective(room,Target);
  Check(h._destination.HasValue,"Rebuilt final approach navigates");
  h=New();h.NavigateObjective(room,Target);room.Instance=11;
  h.NavigateObjective(room,Target);
  Check(h._navRoutes.Stopped==1 && h._objectiveApproachRoom==11,"Room change cancels previous route");
 }
}
'@
Add-Type -TypeDefinition $code
[Harness]::Run()
foreach ($token in @('if (!navigated || _objective.Failure != null) ResetObjectiveApproach();',
    'public void Stop()', '_objectiveTopologyAttempts.Clear();',
    '(_objectiveApproachRoom != room.Instance || EnemyCandidates(room).Any())')) {
 if (-not $d.Contains($token)) { throw "Missing objective cleanup: $token" }
}
Write-Output 'PASS actual objective navigation methods: direct priority, ordered fallback, completion/rebuild, flat rejection, refusal, timeout, bounded retries, interruption and room cleanup'
