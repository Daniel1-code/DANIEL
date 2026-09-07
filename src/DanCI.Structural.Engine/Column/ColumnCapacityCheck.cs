using System;
using System.Collections.Generic;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Geometry;
using DanCI.Structural.Core.Loads;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Core.Units;
using DanCI.Structural.Eurocodes.EC2;
using DanCI.Structural.Reinforcement.Plan;

namespace DanCI.Structural.Engine.Column
{
    /// <summary>
    /// Verification de resistance d'un poteau en flexion composee : diagramme d'interaction
    /// N-M, effets du second ordre par courbure nominale et interaction biaxiale.
    /// Chaque etape produit un <see cref="CheckResult"/> tracable.
    /// </summary>
    public sealed class ColumnCapacityCheck
    {
        private const string CodeName = "EN 1992-1-1:2004";

        private readonly ConcreteProperties _materials;
        private readonly InteractionDiagram _diagram;

        public ColumnCapacityCheck(ConcreteProperties materials)
        {
            _materials = materials;
            _diagram = new InteractionDiagram(materials);
        }

        /// <summary>Taux de travail de la derniere verification menee.</summary>
        public double Utilization { get; private set; }

        /// <summary>La section resiste-t-elle ?</summary>
        public bool Passes { get; private set; }

        /// <summary>
        /// Construit la vue de la section pour une flexion autour de l'axe X (fibre comprimee
        /// perpendiculaire a Y) ou autour de l'axe Y.
        /// </summary>
        public static BendingSection BuildSection(ColumnData column, ColumnReinforcement reinforcement,
                                                  bool aboutX)
        {
            List<BarPosition> bars = ColumnLayoutGeometry.Bars(column, reinforcement);
            BendingSection section;
            double height;

            if (column.Shape == SectionShape.Circular)
            {
                section = BendingSection.Circular(column.DiameterMm);
                height = column.DiameterMm;
            }
            else
            {
                height = aboutX ? column.DepthMm : column.WidthMm;
                double width = aboutX ? column.WidthMm : column.DepthMm;
                section = BendingSection.Rectangular(height, width);
            }

            foreach (BarPosition bar in bars)
            {
                double coordinate = aboutX ? bar.YMm : bar.XMm;
                section.BarDepthsMm.Add(height / 2.0 - coordinate);
                section.BarAreasMm2.Add(bar.AreaMm2);
            }
            return section;
        }

        /// <summary>
        /// Verifie la section sous la combinaison donnee et ajoute les resultats a
        /// <paramref name="checks"/>. Renvoie false si la section ne resiste pas.
        /// </summary>
        public bool Verify(ColumnData column, ColumnReinforcement reinforcement,
                           LoadCombination combination, double bucklingFactor,
                           double creepCoefficient, List<CheckResult> checks, List<string> notes)
        {
            InternalForces forces = combination.Stations[0].Forces;
            double nEd = forces.N;                                   // N
            if (nEd <= 0)
            {
                Passes = true;
                Utilization = 0.0;
                return true;
            }

            double heightX = column.HeightForBendingAboutX;
            double heightY = column.HeightForBendingAboutY;
            double grossArea = column.GrossAreaMm2;
            double steelArea = reinforcement.SteelAreaMm2;

            // --- Excentricite minimale, EC2 6.1(4) ---
            double e0X = SecondOrder.MinimumEccentricityMm(heightX);
            double e0Y = SecondOrder.MinimumEccentricityMm(heightY);
            double mEdX = Math.Max(forces.MomentAboutX, nEd * e0X);
            double mEdY = Math.Max(forces.MomentAboutY, nEd * e0Y);
            notes.Add(string.Format(
                "Excentricite minimale e0 = max(h/30 ; 20 mm) : {0:0} mm suivant X, {1:0} mm suivant Y " +
                "(EC2 6.1(4))", e0X, e0Y));

            // --- Elancement, EC2 5.8.3.1 ---
            double bucklingLength = bucklingFactor * column.HeightMm;
            double slendernessX = SecondOrder.Slenderness(bucklingLength, heightX);
            double slendernessY = SecondOrder.Slenderness(bucklingLength, heightY);
            double relativeAxial = nEd / (grossArea * _materials.Fcd);
            double mechanicalRatio = steelArea * _materials.Fyd / (grossArea * _materials.Fcd);
            double limit = SecondOrder.SlendernessLimit(relativeAxial, mechanicalRatio, creepCoefficient);

            double maxSlenderness = Math.Max(slendernessX, slendernessY);
            var slendernessCheck = new CheckResult
            {
                Code = CodeName,
                Clause = "5.8.3.1",
                Equation = "lambda_lim = 20 A B C / sqrt(n)",
                Description = "Elancement limite",
                Demand = Quantity.Ratio(maxSlenderness),
                Resistance = Quantity.Ratio(limit),
                Utilization = limit > 0 ? maxSlenderness / limit : 0.0,
                Status = maxSlenderness <= limit ? CheckStatus.Pass : CheckStatus.Warning,
                GoverningCombination = combination.Id,
                Comment = maxSlenderness <= limit
                    ? "Les effets du second ordre peuvent etre negliges."
                    : "Poteau elance : les effets du second ordre sont pris en compte."
            };
            slendernessCheck.WithInput("lambda_x", Quantity.Ratio(slendernessX))
                            .WithInput("lambda_y", Quantity.Ratio(slendernessY))
                            .WithInput("l0", Quantity.Length(bucklingLength))
                            .WithInput("n", Quantity.Ratio(relativeAxial));
            checks.Add(slendernessCheck);

            // --- Second ordre, EC2 5.8.8 ---
            if (maxSlenderness > limit)
            {
                if (slendernessX > limit)
                {
                    BendingSection sectionX = BuildSection(column, reinforcement, true);
                    SecondOrderResult second = SecondOrder.NominalCurvature(
                        nEd, bucklingLength, Math.Max(sectionX.EffectiveDepthMm, 0.6 * heightX),
                        slendernessX, relativeAxial, mechanicalRatio, _materials.SteelYieldStrain,
                        _materials.Fck, creepCoefficient);
                    mEdX += second.SecondOrderMomentNmm;
                    notes.Add("Suivant X - " + second.Justification);
                }
                if (slendernessY > limit)
                {
                    BendingSection sectionY = BuildSection(column, reinforcement, false);
                    SecondOrderResult second = SecondOrder.NominalCurvature(
                        nEd, bucklingLength, Math.Max(sectionY.EffectiveDepthMm, 0.6 * heightY),
                        slendernessY, relativeAxial, mechanicalRatio, _materials.SteelYieldStrain,
                        _materials.Fck, creepCoefficient);
                    mEdY += second.SecondOrderMomentNmm;
                    notes.Add("Suivant Y - " + second.Justification);
                }
            }

            // --- Resistance de la section ---
            double axialResistance = _diagram.AxialResistance(grossArea, steelArea);
            double mRdX = _diagram.MomentResistance(BuildSection(column, reinforcement, true), nEd);
            double mRdY = _diagram.MomentResistance(BuildSection(column, reinforcement, false), nEd);

            if (mRdX < 0 || mRdY < 0)
            {
                var axialCheck = new CheckResult
                {
                    Code = CodeName,
                    Clause = "6.1",
                    Equation = "N_Rd = A_c f_cd + A_s f_yd",
                    Description = "Effort normal resistant",
                    GoverningCombination = combination.Id,
                    Comment = "NEd depasse la capacite de la section : aucune resistance en " +
                              "flexion n'est disponible."
                };
                axialCheck.Verify(Quantity.Force(UnitConverter.NToKn(nEd)),
                                  Quantity.Force(UnitConverter.NToKn(axialResistance)));
                checks.Add(axialCheck);
                Passes = false;
                Utilization = axialCheck.Utilization;
                return false;
            }

            // --- Interaction biaxiale, EC2 5.8.9(4) ---
            double exponent = SecondOrder.BiaxialExponent(nEd / axialResistance);
            double utilization = Math.Pow(mEdX / Math.Max(mRdX, 1e-9), exponent)
                                 + Math.Pow(mEdY / Math.Max(mRdY, 1e-9), exponent);

            var biaxial = new CheckResult
            {
                Code = CodeName,
                Clause = "5.8.9 (4)",
                Equation = "(MEdx/MRdx)^a + (MEdy/MRdy)^a <= 1,0",
                Description = "Flexion composee biaxiale",
                Demand = Quantity.Ratio(utilization),
                Resistance = Quantity.Ratio(1.0),
                Utilization = utilization,
                Status = utilization <= 1.0 ? CheckStatus.Pass : CheckStatus.Fail,
                GoverningCombination = combination.Id
            };
            biaxial.WithInput("NEd", Quantity.Force(UnitConverter.NToKn(nEd)))
                   .WithInput("MEd,x", Quantity.Moment(UnitConverter.NmmToKnm(mEdX)))
                   .WithInput("MRd,x", Quantity.Moment(UnitConverter.NmmToKnm(mRdX)))
                   .WithInput("MEd,y", Quantity.Moment(UnitConverter.NmmToKnm(mEdY)))
                   .WithInput("MRd,y", Quantity.Moment(UnitConverter.NmmToKnm(mRdY)))
                   .WithInput("a", Quantity.Ratio(exponent));
            checks.Add(biaxial);

            Passes = utilization <= 1.0;
            Utilization = utilization;
            return Passes;
        }
    }
}
