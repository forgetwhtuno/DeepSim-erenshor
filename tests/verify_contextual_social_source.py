from pathlib import Path
import re

deep = Path(__file__).resolve().parents[1]
mods = deep.parent
camp = mods / "Erenshor-Campmaster"
checks = []

def need(path: Path, pattern: str, label: str, flags=0):
    text = path.read_text(encoding="utf-8-sig")
    ok = re.search(pattern, text, flags) is not None
    checks.append((label, ok))

need(camp / "src/SocialActivityTracker.cs", r"enum\s+CampSocialActivityState.*?Combat.*?Travel.*?ActiveGameplay.*?SocialDowntime.*?ExtendedDowntime", "bounded activity state model", re.S)
need(camp / "src/SocialActivityTracker.cs", r"SocialDowntimeSeconds\s*=\s*60", "60-second initial Auto Relax threshold")
need(camp / "src/SocialActivityTracker.cs", r"TravelHoldSeconds", "movement hysteresis")
need(camp / "src/CampmasterPlugin.cs", r"RecognitionSource\s*==\s*RelaxRecognitionSource\.Automatic.*?CampLivingMode\.None", "Auto Relax cannot start living activities", re.S)
need(camp / "src/CampmasterPlugin.cs", r"PromoteAutomaticToExplicit", "manual Relax precedence")
need(camp / "src/CampmasterApi.cs", r"GetCurrentSocialContext", "read-only social context contract")
need(camp / "src/CampmasterApi.cs", r"autoRelaxActive", "Auto Relax exposed fail-soft")
need(camp / "src/CampmasterPlugin.cs", r"PluginVersion\s*=\s*\"0\.4\.0\"", "Campmaster version preserved")

need(deep / "src/SocialSituation.cs", r"Capacity\s*=\s*20", "bounded chat ring")
need(deep / "src/SocialSituation.cs", r"RecentChatChannel\.Shout", "ambient Shout captured")
need(deep / "src/DeepSimsPlugin.cs", r"(?=.*typeof\(ChatLogLine\))(?=.*ChatLogLine\.LogType\.Party)(?=.*ChatLogLine\.LogType\.Shout)(?=.*ChatLogLine\.LogType\.Say)(?=.*ChatLogLine\.LogType\.Guild)(?=.*ChatLogLine\.LogType\.Whisper)", "native typed chat channels observed", re.S)
need(deep / "src/SocialSituation.cs", r"HEARD", "ambient chat fenced as heard")
need(deep / "src/SocialSituation.cs", r"class\s+UnansweredTurnTracker", "unanswered-turn tracking")
need(deep / "src/SocialSituation.cs", r"30.*60", "full-party pulse envelope")
need(deep / "src/DeepSimsPlugin.cs", r"TryEnterLowPriorityInferenceAsync\(RequestLane\.Autonomous\)", "pulse uses low-priority inference lane")
need(deep / "src/DeepSimsPlugin.cs", r"private readonly SemaphoreSlim _inferenceGate = new SemaphoreSlim\(1, 1\)", "single inference semaphore")
need(deep / "src/DeepSimsPlugin.cs", r"Charge the rolling visible-message.*?if \(line\.Autonomous\).*?TryAdmitAutonomousMessage.*?WriteChat", "visible budget charged only at final boundary", re.S)
need(deep / "src/SocialDirector.cs", r"ReadSocialContext\(\).*?UpdateSoftDowntime", "Campmaster authority with standalone fallback", re.S)
need(deep / "src/SocialDirector.cs", r"context_chose_silence", "pulse silence terminal diagnostic")
need(deep / "src/SocialDirector.cs", r"Direct player conversation|NotePlayerConversation", "player priority retained")
need(deep / "src/DeepSimsPlugin.cs", r"PluginVersion\s*=\s*\"0\.8\.2\"", "Deep Sims version preserved")

failed = [label for label, ok in checks if not ok]
for label, ok in checks:
    print(("PASS: " if ok else "FAIL: ") + label)
if failed:
    raise SystemExit(f"contextual social source verification failed: {len(failed)} checks")
print(f"PASS: contextual social source verification ({len(checks)} checks)")
