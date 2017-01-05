using System;
using Server;
using Server.Spells;
using Server.Network;
using Server.Mobiles;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Server.Items;
/*"Conduit" with 120 Necromancy/120 Spirit Speak places 4 Skull Candles in a 5x5 tile radius for 10 seconds,
 * and if one of the creatures in the radius is hit with a single target Necro spell, then all creatures in 
 * the radius will be affected at the same time, although at 80% normal strength. For example, you can get a
 * bunch of monsters inside the radius, then Corpse Skin all of them simultaneously to reduce their Fire/Poison 
 * Resist by 15 (useful for Fire Horns or Hit Fire Area weps). You can also Strangulate all of them simultaneously 
 * to apply a DoT, or use Poison Strike to have a sort of "Poison Cluster Bomb" go off, where every single creature 
 * in the radius get's hit with the main Poison Strike, as well as multiple secondary strikes if they're bunched up.

Unfortunately, for some reason Conduit will drop any non SC Wep you're wielding, into your pack, which no other Necro spell does.
 
 The necromancer creates a conduit field at a targeted location that causes all targeted necromancy spells to effect 
 * all valid targets within the field at reduced spell strength based on necromancy skill, spirit speak skill, and mastery level.*/ 
namespace Server.Spells.SkillMasteries
{
	public class ConduitSpell : SkillMasterySpell
	{
		private static SpellInfo m_Info = new SpellInfo(
				"Conduit", "Uus Corp Grav",
				204,
				9061,
                Reagent.NoxCrystal,
                Reagent.BatWing,
                Reagent.GraveDust
			);

		public override double RequiredSkill{ get { return 90; } }
		public override double UpKeep { get { return 0; } }
		public override int RequiredMana{ get { return 40; } }
		public override bool PartyEffects { get { return false; } }

        public override SkillName CastSkill { get { return SkillName.Necromancy; } }
        public override SkillName DamageSkill { get { return SkillName.SpiritSpeak; } }

        public int Strength { get; set; }
        public List<Item> Skulls { get; set; }
        public Rectangle2D Zone { get; set; }

        public ConduitSpell(Mobile caster, Item scroll)
            : base(caster, scroll, m_Info)
		{
		}

        public override void OnBeginCast()
        {
            base.OnBeginCast();

            Effects.SendLocationParticles(EffectItem.Create(Caster.Location, Caster.Map, EffectItem.DefaultDuration), 0x36CB, 1, 14, 0x55C, 7, 9915, 0);
        }

		public override void OnCast()
		{
            Caster.Target = new MasteryTarget(this, 10, true, Server.Targeting.TargetFlags.None);
		}

        protected override void OnTarget(object o)
        {
            IPoint3D p = o as IPoint3D;

            if (p != null && CheckSequence())
            {
                Rectangle2D rec = new Rectangle2D(p.X - 3, p.Y - 3, 6, 6);
                Skulls = new List<Item>();

                Item skull = new InternalItem();
                skull.MoveToWorld(new Point3D(rec.X, rec.Y, Caster.Map.GetAverageZ(rec.X, rec.Y)), Caster.Map);
                Skulls.Add(skull);

                skull = new InternalItem();
                skull.MoveToWorld(new Point3D(rec.X + rec.Width, rec.Y + rec.Height, Caster.Map.GetAverageZ(rec.X + rec.Width, rec.Y + rec.Height)), Caster.Map);
                Skulls.Add(skull);

                skull = new InternalItem();
                skull.MoveToWorld(new Point3D(rec.X + rec.Width, rec.Y, Caster.Map.GetAverageZ(rec.X + rec.Width, rec.Y)), Caster.Map);
                Skulls.Add(skull);

                skull = new InternalItem();
                skull.MoveToWorld(new Point3D(rec.X, rec.Y + rec.Height, Caster.Map.GetAverageZ(rec.X, rec.Y + rec.Height)), Caster.Map);
                Skulls.Add(skull);

                skull = new InternalItem();
                skull.MoveToWorld(new Point3D(rec.X + (rec.Width / 2), rec.Y + (rec.Height / 2), Caster.Map.GetAverageZ(rec.X + (rec.Width / 2), rec.Y + (rec.Height / 2))), Caster.Map);
                Skulls.Add(skull);

                Zone = rec;
                Strength = (int)((Caster.Skills[CastSkill].Value + Caster.Skills[DamageSkill].Value + (GetMasteryLevel() * 20)) / 3.75);
                Expires = DateTime.UtcNow + TimeSpan.FromSeconds(10);

                BuffInfo.AddBuff(Caster, new BuffInfo(BuffIcon.Conduit, 1155901, 1156053, Strength.ToString())); //Targeted Necromancy spells used on a target within the Conduit field will affect all valid targets within the field at ~1_PERCT~% strength. 

                BeginTimer();
            }
        }

        public override void EndEffects()
        {
            Skulls.Where(i => i != null && !i.Deleted).ForEach(i => i.Delete());
            Skulls.Free();

            BuffInfo.RemoveBuff(Caster, BuffIcon.Conduit);
        }

        public static bool CheckAffected(Mobile caster, IDamageable victim, Action<Mobile, double> callback)
        {
            foreach (SkillMasterySpell spell in EnumerateSpells(caster, typeof(ConduitSpell)))
            {
                ConduitSpell conduit = spell as ConduitSpell;

                if (conduit == null)
                    continue;

                if (conduit.Zone.Contains(victim))
                {
                    IPooledEnumerable eable = victim.Map.GetMobilesInBounds(conduit.Zone);
                    List<Mobile> toAffect = null;

                    foreach (Mobile m in eable)
                    {
                        if (m != victim && conduit.Caster.CanBeHarmful(m))
                        {
                            if (toAffect == null)
                                toAffect = new List<Mobile>();

                            toAffect.Add(m);
                        }
                    }

                    eable.Free();

                    if (toAffect != null && callback != null)
                    {
                        toAffect.ForEach(m => callback(m, (double)conduit.Strength / 100.0));
                        toAffect.Free();
                        return true;
                    }
                }
            }

            return false;
        }

        /*public static double ModifiedStrength(Mobile caster, Mobile victim)
        {
            List<SkillMasterySpell> spells = GetSpells(caster).Where(s => s.GetType() == typeof(ConduitSpell));

            foreach (SkillMasterySpell spell in spells)
            {
                if (spell.Zone.Contains(victim))
                    return (double)spell.Strength / 100;
            }

            return 1.0;
        }*/

        private class InternalItem : Item
        {
            public InternalItem()
                : base(Utility.RandomList(0x1853, 0x1858))
            {
            }

            public InternalItem(Serial serial)
                : base(serial)
            {
            }

            public override void Serialize(GenericWriter writer)
            {
                base.Serialize(writer);

                writer.Write((int)0); // version
            }

            public override void Deserialize(GenericReader reader)
            {
                base.Deserialize(reader);

                int version = reader.ReadInt();

                Delete();
            }
        }
	}
}