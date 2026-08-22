using System.Collections.ObjectModel;
using System.Numerics;
using TacticalSim.Core.Physiology;
using TacticalSim.Core.Units;

namespace TacticalSim.Core.Damage.Anatomy;

public enum AnatomicalStructureType { Organ, Artery, Vein, Bone, Nerve, Airway, Pleura, Pericardium, Skin, Fascia }
public enum PressureRegime { None, Arterial, Venous, Pulmonary, Parenchymal }
public enum FunctionalRole { None, WeightBearing, UpperLimbMotor, LowerLimbMotor, SpinalCord, Airway, Cardiac, Respiratory }

/// <summary>A rendering-independent, versioned anatomical object in body-local metres.</summary>
public sealed record AnatomicalStructure
{
    public const string CurrentDefinitionVersion = "anatomy-m6-v1";

    public AnatomicalStructure(
        string id, string displayName, AnatomicalStructureType type, BodyPartType region,
        Vector3 start, Vector3 end, Distance radius, string definitionVersion = CurrentDefinitionVersion,
        Distance? calibre = null, PressureRegime pressureRegime = PressureRegime.None,
        FunctionalRole functionalRole = FunctionalRole.None, string? laterality = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionVersion);
        if (!IsFinite(start) || !IsFinite(end)) throw new ArgumentOutOfRangeException(nameof(start));
        if (radius.Meters <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
        if (calibre is { Meters: <= 0 }) throw new ArgumentOutOfRangeException(nameof(calibre));
        Id = id; DisplayName = displayName; Type = type; Region = region; Start = start; End = end;
        Radius = radius; DefinitionVersion = definitionVersion; Calibre = calibre ?? radius + radius;
        PressureRegime = pressureRegime; FunctionalRole = functionalRole; Laterality = laterality;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public AnatomicalStructureType Type { get; }
    public BodyPartType Region { get; }
    public Vector3 Start { get; }
    public Vector3 End { get; }
    public Distance Radius { get; }
    public Distance Calibre { get; }
    public PressureRegime PressureRegime { get; }
    public FunctionalRole FunctionalRole { get; }
    public string? Laterality { get; }
    public string DefinitionVersion { get; }

    public bool IntersectsSegment(Vector3 from, Vector3 to, float extraRadiusMeters = 0f) =>
        SegmentDistanceSquared(from, to, Start, End) <= MathF.Pow(Radius.Meters + MathF.Max(0, extraRadiusMeters), 2);

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    // Closest distance between two line segments (Real-Time Collision Detection, deterministic clamped form).
    private static float SegmentDistanceSquared(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
    {
        const float epsilon = 1e-8f;
        Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
        float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r), s, t;
        if (a <= epsilon && e <= epsilon) return Vector3.DistanceSquared(p1, p2);
        if (a <= epsilon) { s = 0; t = Math.Clamp(f / e, 0, 1); }
        else
        {
            float c = Vector3.Dot(d1, r);
            if (e <= epsilon) { t = 0; s = Math.Clamp(-c / a, 0, 1); }
            else
            {
                float b = Vector3.Dot(d1, d2), denominator = a * e - b * b;
                s = denominator == 0 ? 0 : Math.Clamp((b * f - c * e) / denominator, 0, 1);
                t = (b * s + f) / e;
                if (t < 0) { t = 0; s = Math.Clamp(-c / a, 0, 1); }
                else if (t > 1) { t = 1; s = Math.Clamp((b - c) / a, 0, 1); }
            }
        }
        return Vector3.DistanceSquared(p1 + d1 * s, p2 + d2 * t);
    }
}

public interface IAnatomicalStructureCatalog
{
    string DefinitionVersion { get; }
    IReadOnlyList<AnatomicalStructure> Structures { get; }
    AnatomicalStructure GetRequired(string id);
    IReadOnlyList<AnatomicalStructure> QuerySegment(Vector3 start, Vector3 end, float radiusMeters = 0f);
}

public sealed class AnatomicalStructureCatalog : IAnatomicalStructureCatalog
{
    private readonly ReadOnlyCollection<AnatomicalStructure> _structures;
    private readonly IReadOnlyDictionary<string, AnatomicalStructure> _byId;
    public AnatomicalStructureCatalog(IEnumerable<AnatomicalStructure> structures, string definitionVersion = AnatomicalStructure.CurrentDefinitionVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionVersion);
        DefinitionVersion = definitionVersion;
        AnatomicalStructure[] ordered = structures.OrderBy(s => s.Id, StringComparer.Ordinal).ToArray();
        if (ordered.Any(s => s.DefinitionVersion != definitionVersion)) throw new ArgumentException("All structures must use the catalog definition version.", nameof(structures));
        if (ordered.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != ordered.Length) throw new ArgumentException("Structure IDs must be unique.", nameof(structures));
        _structures = Array.AsReadOnly(ordered); _byId = ordered.ToDictionary(s => s.Id, StringComparer.Ordinal);
    }
    public string DefinitionVersion { get; }
    public IReadOnlyList<AnatomicalStructure> Structures => _structures;
    public AnatomicalStructure GetRequired(string id) => _byId.TryGetValue(id, out var value) ? value : throw new KeyNotFoundException($"Unknown anatomical structure '{id}'.");
    public IReadOnlyList<AnatomicalStructure> QuerySegment(Vector3 start, Vector3 end, float radiusMeters = 0f) =>
        _structures.Where(s => s.IntersectsSegment(start, end, radiusMeters)).ToArray();
}

/// <summary>First-pass named structures. Coordinates match <see cref="AnatomicalDummyBuilder"/>.</summary>
public static class StandardAnatomy
{
    public static IAnatomicalStructureCatalog CreateCatalog()
    {
        var s = new List<AnatomicalStructure>();
        void Vessel(string id, string name, AnatomicalStructureType type, BodyPartType region, Vector3 a, Vector3 b, float diameter, PressureRegime pressure, string? side = null) =>
            s.Add(new(id, name, type, region, a, b, Distance.FromMeters(diameter / 2), calibre: Distance.FromMeters(diameter), pressureRegime: pressure, laterality: side));
        void Solid(string id, string name, AnatomicalStructureType type, BodyPartType region, Vector3 a, Vector3 b, float radius, FunctionalRole role = FunctionalRole.None, string? side = null) =>
            s.Add(new(id, name, type, region, a, b, Distance.FromMeters(radius), functionalRole: role, laterality: side));

        Vessel("vessel.aorta", "Aorta", AnatomicalStructureType.Artery, BodyPartType.Thorax, new(0, .42f, -.045f), new(0, .08f, -.045f), .024f, PressureRegime.Arterial);
        Vessel("vessel.vena-cava", "Vena cava", AnatomicalStructureType.Vein, BodyPartType.Thorax, new(.025f, .42f, -.04f), new(.025f, .08f, -.04f), .022f, PressureRegime.Venous);
        AddPairedVessels(s, "carotid", BodyPartType.Neck, new(.025f,.52f,-.01f), new(.025f,.65f,-.01f), .007f, PressureRegime.Arterial);
        AddPairedVessels(s, "jugular", BodyPartType.Neck, new(.04f,.52f,0), new(.04f,.65f,0), .009f, PressureRegime.Venous);
        AddLimbChain(s, true); AddLimbChain(s, false);
        Vessel("vessel.pulmonary-hilum", "Pulmonary hilar vessels", AnatomicalStructureType.Artery, BodyPartType.Thorax, new(-.06f,.34f,-.03f), new(.06f,.34f,-.03f), .014f, PressureRegime.Pulmonary);
        Vessel("vessel.hepatic-pedicle", "Hepatic vascular pedicle", AnatomicalStructureType.Artery, BodyPartType.Abdomen, new(-.07f,.16f,-.03f), new(-.02f,.16f,-.03f), .010f, PressureRegime.Arterial);
        Vessel("vessel.splenic-pedicle", "Splenic vascular pedicle", AnatomicalStructureType.Artery, BodyPartType.Abdomen, new(.08f,.14f,-.02f), new(.13f,.14f,-.02f), .008f, PressureRegime.Arterial);
        AddPairedVessels(s, "renal-pedicle", BodyPartType.Abdomen, new(.04f,.12f,-.05f), new(.12f,.12f,-.05f), .007f, PressureRegime.Arterial);

        Solid("bone.pelvis", "Pelvic ring", AnatomicalStructureType.Bone, BodyPartType.Abdomen, new(-.13f,.02f,-.05f), new(.13f,.02f,-.05f), .025f, FunctionalRole.WeightBearing);
        Solid("bone.sternum", "Sternum", AnatomicalStructureType.Bone, BodyPartType.Thorax, new(0,.22f,.12f), new(0,.46f,.12f), .012f);
        for (int rib = 1; rib <= 12; rib++)
        {
            float y = .47f - rib * .021f;
            Solid($"bone.rib-{rib:D2}-left", $"left rib {rib}", AnatomicalStructureType.Bone, BodyPartType.Thorax, new(-.19f,y,0), new(-.03f,y,.11f), .006f, side:"left");
            Solid($"bone.rib-{rib:D2}-right", $"right rib {rib}", AnatomicalStructureType.Bone, BodyPartType.Thorax, new(.03f,y,.11f), new(.19f,y,0), .006f, side:"right");
        }
        Solid("bone.skull", "Skull", AnatomicalStructureType.Bone, BodyPartType.Head, new(0,.69f,-.02f), new(0,.85f,-.02f), .075f);
        AddPairedBones(s, "femur", BodyPartType.LeftLeg, new(.1f,-.02f,0), new(.1f,-.42f,0), .018f, FunctionalRole.WeightBearing);
        AddPairedBones(s, "tibia", BodyPartType.LeftLeg, new(.1f,-.42f,0), new(.1f,-.78f,0), .014f, FunctionalRole.WeightBearing);
        AddPairedBones(s, "humerus", BodyPartType.LeftArm, new(.3f,.48f,0), new(.3f,.25f,0), .013f, FunctionalRole.UpperLimbMotor);
        AddPairedBones(s, "radius-ulna", BodyPartType.LeftArm, new(.3f,.25f,0), new(.3f,.02f,0), .012f, FunctionalRole.UpperLimbMotor);
        Solid("bone.spine", "Cervical and thoracolumbar spine", AnatomicalStructureType.Bone, BodyPartType.Thorax, new(0,.04f,-.10f), new(0,.65f,-.08f), .022f, FunctionalRole.WeightBearing);
        Solid("nerve.spinal-cord", "Spinal cord", AnatomicalStructureType.Nerve, BodyPartType.Thorax, new(0,.04f,-.10f), new(0,.65f,-.08f), .007f, FunctionalRole.SpinalCord);
        AddPairedNerves(s, "brachial-plexus", BodyPartType.LeftArm, new(.12f,.43f,-.02f), new(.3f,.25f,-.02f), FunctionalRole.UpperLimbMotor);
        AddPairedNerves(s, "sciatic", BodyPartType.LeftLeg, new(.1f,.02f,-.05f), new(.1f,-.70f,-.05f), FunctionalRole.LowerLimbMotor);
        Solid("airway.trachea", "Trachea", AnatomicalStructureType.Airway, BodyPartType.Neck, new(0,.48f,-.015f), new(0,.65f,-.015f), .012f, FunctionalRole.Airway);
        Solid("boundary.pleura-left", "Left pleura", AnatomicalStructureType.Pleura, BodyPartType.Thorax, new(-.10f,.22f,0), new(-.10f,.45f,0), .075f, FunctionalRole.Respiratory, "left");
        Solid("boundary.pleura-right", "Right pleura", AnatomicalStructureType.Pleura, BodyPartType.Thorax, new(.10f,.22f,0), new(.10f,.45f,0), .075f, FunctionalRole.Respiratory, "right");
        Solid("boundary.pericardium", "Pericardium", AnatomicalStructureType.Pericardium, BodyPartType.Thorax, new(0,.30f,0), new(0,.37f,0), .055f, FunctionalRole.Cardiac);
        Solid("organ.heart", "Heart", AnatomicalStructureType.Organ, BodyPartType.Thorax, new(-.025f,.28f,0), new(-.025f,.38f,0), .05f, FunctionalRole.Cardiac);
        Solid("organ.lung-left", "Left lung", AnatomicalStructureType.Organ, BodyPartType.Thorax, new(-.11f,.22f,0), new(-.11f,.44f,0), .065f, FunctionalRole.Respiratory, "left");
        Solid("organ.lung-right", "Right lung", AnatomicalStructureType.Organ, BodyPartType.Thorax, new(.11f,.22f,0), new(.11f,.44f,0), .065f, FunctionalRole.Respiratory, "right");
        Solid("organ.liver", "Liver", AnatomicalStructureType.Organ, BodyPartType.Abdomen, new(-.10f,.10f,0), new(-.03f,.18f,0), .065f);
        Solid("organ.spleen", "Spleen", AnatomicalStructureType.Organ, BodyPartType.Abdomen, new(.09f,.10f,0), new(.13f,.17f,0), .035f);
        Solid("organ.brain", "Brain", AnatomicalStructureType.Organ, BodyPartType.Head, new(0,.70f,-.03f), new(0,.84f,-.03f), .06f);
        Solid("boundary.skin-torso", "Torso skin envelope", AnatomicalStructureType.Skin, BodyPartType.Thorax, new(0,.02f,0), new(0,.49f,0), .21f);
        Solid("boundary.thoracolumbar-fascia", "Thoracolumbar fascia", AnatomicalStructureType.Fascia, BodyPartType.Thorax, new(0,.05f,-.13f), new(0,.48f,-.13f), .015f);
        return new AnatomicalStructureCatalog(s);
    }

    private static void AddPairedVessels(List<AnatomicalStructure> s, string name, BodyPartType region, Vector3 a, Vector3 b, float d, PressureRegime p)
    { foreach (float sign in new[]{-1f,1f}) { string side=sign<0?"left":"right"; Vector3 aa=a with { X=a.X*sign }, bb=b with { X=b.X*sign }; s.Add(new($"vessel.{name}-{side}", $"{side} {name}", p==PressureRegime.Venous?AnatomicalStructureType.Vein:AnatomicalStructureType.Artery, region, aa, bb, Distance.FromMeters(d/2), calibre:Distance.FromMeters(d), pressureRegime:p, laterality:side)); } }
    private static void AddLimbChain(List<AnatomicalStructure> s, bool left)
    { float sign=left?-1:1; string side=left?"left":"right"; BodyPartType arm=left?BodyPartType.LeftArm:BodyPartType.RightArm, leg=left?BodyPartType.LeftLeg:BodyPartType.RightLeg; void V(string n, BodyPartType r, Vector3 a, Vector3 b, float d) => s.Add(new($"vessel.{n}-{side}",$"{side} {n}",AnatomicalStructureType.Artery,r,a,b,Distance.FromMeters(d/2),calibre:Distance.FromMeters(d),pressureRegime:PressureRegime.Arterial,laterality:side)); V("subclavian",arm,new(.03f*sign,.44f,-.03f),new(.19f*sign,.43f,-.03f),.009f); V("axillary",arm,new(.19f*sign,.43f,-.03f),new(.3f*sign,.35f,-.03f),.008f); V("brachial",arm,new(.3f*sign,.35f,-.03f),new(.3f*sign,.05f,-.03f),.006f); V("iliac",leg,new(.02f*sign,.08f,-.04f),new(.1f*sign,-.05f,-.04f),.010f); V("femoral",leg,new(.1f*sign,-.05f,-.04f),new(.1f*sign,-.38f,-.04f),.009f); V("popliteal",leg,new(.1f*sign,-.38f,-.04f),new(.1f*sign,-.55f,-.04f),.006f); }
    private static void AddPairedBones(List<AnatomicalStructure> s,string name,BodyPartType _,Vector3 a,Vector3 b,float r,FunctionalRole role) { foreach(float sign in new[]{-1f,1f}) { string side=sign<0?"left":"right"; BodyPartType region=name is "femur" or "tibia"?(sign<0?BodyPartType.LeftLeg:BodyPartType.RightLeg):(sign<0?BodyPartType.LeftArm:BodyPartType.RightArm); s.Add(new($"bone.{name}-{side}",$"{side} {name}",AnatomicalStructureType.Bone,region,a with {X=a.X*sign},b with {X=b.X*sign},Distance.FromMeters(r),functionalRole:role,laterality:side)); } }
    private static void AddPairedNerves(List<AnatomicalStructure> s,string name,BodyPartType _,Vector3 a,Vector3 b,FunctionalRole role) { foreach(float sign in new[]{-1f,1f}) { string side=sign<0?"left":"right"; BodyPartType region=role==FunctionalRole.UpperLimbMotor?(sign<0?BodyPartType.LeftArm:BodyPartType.RightArm):(sign<0?BodyPartType.LeftLeg:BodyPartType.RightLeg); s.Add(new($"nerve.{name}-{side}",$"{side} {name}",AnatomicalStructureType.Nerve,region,a with {X=a.X*sign},b with {X=b.X*sign},Distance.FromMeters(.005f),functionalRole:role,laterality:side)); } }
}
