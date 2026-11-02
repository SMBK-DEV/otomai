using System.Diagnostics;
using System.Text.Json;
using Bubble.Core.Datacenter.Datacenter.Effects;
using Bubble.Core.Datacenter.Datacenter.Spells;
using Bubble.DamageCalculation;
using Bubble.DamageCalculation.Customs;
using Bubble.DamageCalculation.DamageManagement;
using Bubble.DamageCalculation.FighterManagement;
using Bubble.DamageCalculation.SpellManagement;
using Bubble.DamageCalculation.Tools;
using BubbleBot.Cli.Models;
using BubbleBot.Cli.Repository;
using BubbleBot.Cli.Repository.Maps;
using BubbleBot.Cli.Services.Clients.Contracts;
using BubbleBot.Cli.Services.Maps;
using Serilog;
using Direction = BubbleBot.Cli.Repository.Maps.Direction;
using MapTools = Bubble.DamageCalculation.Tools.MapTools;

namespace BubbleBot.Cli.Services.Fight;

public enum AiStep
{
    NotStarted,
    CastSummon,
    CastBoost,
    CastSpell,   
    CastSpell2,
    Move,
    ReCastAfterMove,
    ReCastAfterMove2,
    Move2,
    ReCastAfterMove3,
}

public class FightInfo
{
    public const bool NoAnim = false;
    public IFightClientContext Client { get; }
    public Map Map { get; set; }

    public AiStep CurrentStep { get; set; }
    public Dictionary<long, FightActor> ActorPositions { get; set; } = new();

    public bool IsInPreparation { get; set; }
    public FightActor? FighterPlaying { get; private set; }
    private List<FighterCellLight> FightersInitialPositions { get; set; } = new();

    public bool IsMyTurnReady { get; set; }
    public int CurrentSequenceDuration { get; set; }
    public int LastSequenceDuration { get; set; }
    public FightInfo(IFightClientContext client, Map map)
    {
        Client = client;
        Map = map;
    }

    private IList<Mark> _marks = new List<Mark>();

    public bool IsEnded { get; set; }

    private int _openedSequence = 0;
    private int _totalSequences = 0;
    private int _illegalActions = 0;
    
    public void OnFightTurnEvent(FightTurnEvent fightTurnEvent)
    {
        FighterPlaying = ActorPositions.GetValueOrDefault(fightTurnEvent.CharacterId);
        _openedSequence = 0;
        _totalSequences = 0;
        LastSequenceDuration = 0;
        IsInPreparation = false;
        if (Turn == 40)
        {
            Client.SendRequest(new SurrenderVoteCastRequest
            {
                Vote = true,
                Vote2 = true
            }, SurrenderVoteCastRequest.TypeUrl);
        }
        
        if (fightTurnEvent.CharacterId != Client.PlayerId)
        {
            return;
        }
        
        if(ActorPositions.Count <= 1)
        {
            PassTurn();
            return;
        }
        
        _castedSpellsPerTarget.Clear();
        _castedSpells.Clear();
        _executedSpells = 0;
        _illegalActions = 0;
        IsMyTurnReady = false;
        _playTokenSource.Cancel();
        var tokenSource = new CancellationTokenSource();
        _playTokenSource = tokenSource;
        
        if(Client.AutoPass)
        {
            PassTurn();
            return;
        }
        
        try
        {    
            Turn++;
            var turnNumber = Turn;
            tokenSource.Token.Register(() =>
            {
                if (turnNumber == Turn && FighterPlaying == Client.Fighter)
                {
                    PassTurn(true);
                }
            });
            tokenSource.CancelAfter(2000);

            CurrentStep = AiStep.CastSummon;
            PlayStep(true, _playTokenSource.Token);
        }
        catch (Exception e)
        {
            Log.Error(e, "Erreur lors de la gestion du tour de combat");
            PassTurn();
        }
        finally { }
    }

    public void OnPlacementPossiblePositionsEvent(
        FightPlacementPossiblePositionsEvent fightPlacementPossiblePositionsEvent)
    {
        foreach (var spell in Client.Spells)
        {
            spell.Reset();
        }
        
        IsInPreparation = true;
        
        if (Client.Party != null && Client.Party.Leader == Client.PlayerId)
        {
            if (Client.Party.Members.Count > 1)
            {
                var allReady = 0;
                foreach (var member in Client.Party.Members)
                {
                    if (member == Client.PlayerId)
                    {
                        continue;
                    }

                    if (ActorPositions.ContainsKey(member))
                    {
                        allReady++;
                    }
                }

                if (Client.Trajet != null && allReady == Client.Trajet.MinGroupsPlayers - 1)
                {
                    Client.SendRequestWithDelay(new FightReadyRequest
                                                {
                                                    IsReady = true
                                                }, FightReadyRequest.TypeUrl,
                                                NoAnim ? 100 : Random.Shared.Next(1000, 2000), (x) => IsInPreparation);
                }
            }
        }
        else
        {
            _ = Client.SendRequestWithDelay(new FightReadyRequest
                                            {
                                                IsReady = true
                                            },
                                            FightReadyRequest.TypeUrl,
                                            NoAnim ? 500 : 2000);
        }
    }

    public void OnFightEndEvent(FightEndEvent fightEndEvent)
    {
        IsEnded = true;
    }

    private int _executedSpells = 0;

    public int CurrentTurnTime = 1000;

    public int Turn;

    private readonly Dictionary<int, int> _castedSpells = new();
    private readonly Dictionary<int, (long target, int count)> _castedSpellsPerTarget = new();

    public int LastCastedSpellId { get; set; }
    public int LastCastedSpellLevelId { get; set; }

    public void OnSequenceStartEvent(SequenceStartEvent sequenceStartEvent)
    {
        if (_openedSequence == 0)
        {
            CurrentSequenceDuration = 0;
        }
        
        _openedSequence++;

        if (_openedSequence >= 1)
        {
            if(sequenceStartEvent.SequenceType != SequenceType.Triggered)
            {
                _totalSequences++;
            }
        }
        
        Log.Information("Ouverture d'une séquence {SequenceId}", sequenceStartEvent.SequenceType);
    }

    public void OnSequenceEndEvent(SequenceEndEvent sequenceEndEvent)
    {
        _openedSequence--;
        Log.Information("Fermeture d'une séquence {SequenceId}", sequenceEndEvent.SequenceType);

        if (_openedSequence == 0)
        {        
            Log.Information("C'est la fin des séquences, on peut jouer, on attend {CurrentSequenceDuration}ms", CurrentSequenceDuration);
            LastSequenceDuration = CurrentSequenceDuration + 100;
            Client.SendRequest(new GameActionAcknowledgementRequest
                               {
                                   Valid = true,
                                   ActionId = sequenceEndEvent.ActionId,
                                   ActionId2 = sequenceEndEvent.ActionId
                               }, GameActionAcknowledgementRequest.TypeUrl);
            _totalSequences = 0;
            CurrentSequenceDuration = 0;
            if (sequenceEndEvent.SequenceType == SequenceType.TurnEnd)
            {
                Log.Information("C'est la fin du tour, on attend {LastSequenceDuration}ms", LastSequenceDuration);

                Client.SendRequestWithDelay(new FightTurnReadyRequest
                                            {
                                                IsReady = true,
                                                IsReady2 = true
                                            },
                                            FightTurnReadyRequest.TypeUrl,
                                            NoAnim ? 100 : LastSequenceDuration,
                                            (msg) => !IsEnded && Client.IsInFight);
            }
        }

        if (MySpellWasCast && FighterPlaying?.Id == Client.PlayerId && _openedSequence == 0)
        {
            SetNextStep();
            PlayStep(false, _playTokenSource.Token);
        }
    }
    
    private CancellationTokenSource _playTokenSource = new();   

    public void OnFightAction(GameActionFightEvent actionFight)
    {
        var json = JsonSerializer.Serialize(actionFight, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        
        Log.Information("Action de combat {ActionType} {ActionFight}", (ActionId)actionFight.ActionId, json);
        // 1 chance sur 200 de show cell sur la case
        
        if (Random.Shared.Next(0, 500) == 100)
        {
            if (actionFight.TargetedAbilityValue != null)
            {
                Client.SendRequest(new ShowCellRequest
                                   {
                                       CellId = actionFight.TargetedAbilityValue.DestinationCell,
                                       Idks = null
                                   },
                                   ShowCellRequest.TypeUrl);
            }
        }
        
        /*if (actionFight.TeleportOnSameMapValue != null)
        {
            var teleport = actionFight.TeleportOnSameMapValue;
            var fighter = ActorPositions.GetValueOrDefault(teleport.TargetId);

            if (fighter != null)
            {
                fighter.CellId = teleport.Cell;
            }
        }

        if (actionFight.SlideValue != null)
        {
            var slide = actionFight.SlideValue;
            var fighter = ActorPositions.GetValueOrDefault(slide.TargetId);

            if (fighter != null)
            {
                fighter.CellId = slide.EndCell;
            }
        }
*/
        if (actionFight.SummonsValue != null)
        {
            if (actionFight.SummonsValue.SummonsByActorValue != null)
            {
                foreach (var summon in actionFight.SummonsValue.SummonsByActorValue.Summons)
                {
                    AddFightActor(summon);
                }
            }     
            if (actionFight.SummonsValue.SummonsByContextInformationValue != null)
            {
                foreach (var summonAction in actionFight.SummonsValue.SummonsByContextInformationValue.Summons)
                {
                    foreach (var summon in summonAction.Summons)
                    {
                        AddFightActor(new ActorPositionInformation
                        {
                            ActorId = summon.Position.ActorId,
                            Disposition = summon.Position.Disposition,
                            ActorInformationValue = new ActorPositionInformation.ActorInformation
                            {
                                Look = summonAction.Look,
                                RolePlayActorValue = null,
                                Fighter = new ActorPositionInformation.ActorInformation.FightFighterInformation
                                {
                                    SpawnInformation = new SpawnInformation
                                    {
                                        Team = summon.Team,
                                        Alive = summon.Alive,
                                        Position = summon.Position
                                    },
                                    Wave = 0,
                                    Stats = summonAction.Characteristics,
                                    PreviousPositions = new List<int>()
                                },
                                Eipps = null
                            }
                        });
                    }

                }
            }     
        }

        /*
        if (actionFight.ExchangePositionsValue != null)
        {
            var exchange = actionFight.ExchangePositionsValue;
            var fighter1 = ActorPositions.GetValueOrDefault(exchange.TargetId);
            var fighter2 = GetFighterAtCell(exchange.CasterCellId);

            if (fighter1 != null && fighter2 != null)
            {
                (fighter1.CellId, fighter2.CellId) = (fighter2.CellId, fighter1.CellId);
            }
        }
        
        if (actionFight.DeathValue != null)
        {
            var death = actionFight.DeathValue;
            var fighter = ActorPositions.GetValueOrDefault(death.TargetId);

            if (fighter != null)
            {
                fighter.IsForcedDead = true;
            }
        }

        if (actionFight.KillValue != null)
        {
            var death = actionFight.KillValue;
            var fighter = ActorPositions.GetValueOrDefault(death.TargetId);

            if (fighter != null)
            {
                fighter.IsForcedDead = true;
            }
        }
*/
        if (actionFight.TargetedAbilityValue != null)
        {
            CurrentSequenceDuration += 200;
        }

        if (actionFight.SourceId == Client.PlayerId)
        {
            if (actionFight.TargetedAbilityValue != null &&
                actionFight.TargetedAbilityValue.SpellCastValue.SpellId == LastCastedSpellId &&
                actionFight.TargetedAbilityValue.SpellCastValue.SpellLevel == LastCastedSpellLevelId)
            {
                if (FighterPlaying?.Id == Client.PlayerId)
                {
                    MySpellWasCast = true;
                }
            }
        }
    }

    public bool MySpellWasCast { get; set; }

    public void OnFightMovement(MapMovementEvent mapMovementEvent)
    {
        var cellId = mapMovementEvent.Cells[^1];
        var actorId = mapMovementEvent.CharacterId;
        var actor = ActorPositions.GetValueOrDefault(actorId);
        
        CurrentSequenceDuration += 50 * mapMovementEvent.Cells.Count;

        if (actor != null)
        {
            actor.CellId = cellId;
            actor.BeforeLastSpellPosition = cellId;
            
            Client.LogInfo("Déplacement d'un joueur sur la cellule {EndCellId} depuis {LastCellId} sur la Map " +
                           actor.Fight!.Map.Id,
                           cellId.ToString(),
                           actor.LastCellId.ToString());
        }

        if (actorId == Client.PlayerId)
        {
            if (FighterPlaying?.Id == Client.PlayerId)
            {
                SetNextStep();
                PlayStep(false, _playTokenSource.Token);
            }
        }
    }


    public void PlayStep(bool first = false, CancellationToken token = default)
    {
        if(token.IsCancellationRequested)
        {
            return;
        }
        
        var myself = ActorPositions.GetValueOrDefault(Client.PlayerId);
        if (myself == null)
        {
            return;
        }    
        
        if(myself.IsForcedDead)
        {
            return;
        }

        if (FighterPlaying?.Id != Client.PlayerId)
        {
            return;
        }
        
        CurrentTurnTime = 1000;
        Client.Fighter = myself;

        
        myself.Spells = Client.Spells.ToList();
        foreach (var spell in myself.Spells)
        {
            spell.Caster = Client;
        }

        myself.SetFighterTranslator();
        
        foreach (var fighter in ActorPositions)
        {
            fighter.Value.SetFighterTranslator();
        }
        var haxeFighter = myself.FighterTranslator.Clone();

        // On prend un sort au hasard de la liste qu'on peut lancé !
        var spells = Client.Spells.ToList();
        
        var environment = new AiEnvironment(myself)
        {
            FightContext = GetFightContextSimulation(haxeFighter),
            HaxeFighter = haxeFighter,
            Enemies = GetEnemies(myself),
            Allies = GetAllies(myself),
            Portals = new List<Mark>(),
            Spells = spells,
            CanMove = myself.Stats.Mp.Base > 0,
            IsFightEnded = IsFightEnded(),
        };

        if (environment.Fighter.Breed > 0 && environment.Fighter.Breed <= 23)
        {
            environment.ForceElementId = environment.HaxeFighter.GetBestElement();
            Task.Run(async () =>
            {       
                var sw = Stopwatch.StartNew();

                if(!first && !NoAnim)
                {
                    await Task.Delay(100, token);
                }
                
                await PlayerPlay(environment, token);
                
                sw.Stop();
                Log.Logger.Information("Temps de calcul de l'IA {Time}ms", sw.ElapsedMilliseconds);

            }, token);
        }
        
    }

    private readonly List<AiStep> _stepOrder = new()
    {
        AiStep.CastSummon,
        AiStep.CastBoost,
        AiStep.CastSpell,      
        AiStep.CastSpell2,
        AiStep.Move,
        AiStep.ReCastAfterMove,
        AiStep.ReCastAfterMove2,
        AiStep.Move2,
        AiStep.ReCastAfterMove3,
    };
    
    private bool _hasMoved = false;
    private bool _hasCastedSpell = false;

    private async Task PlayerPlay(AiEnvironment environment, CancellationToken token = default)
    {
        if (token.IsCancellationRequested)
        {
            return;
        }
        
        _hasMoved = false;
        _hasCastedSpell = false;
        MySpellWasCast = false;
        
        Log.Information("Il y'a {Ennemies} à la Step {CurrentStep}", environment.Enemies.Count, CurrentStep);

        if (environment.Enemies.Count == 0 || !Client.IsInFight)
        {
            PassTurn(true);
            return;
        }
        
        switch (CurrentStep)
        {
            case AiStep.CastSummon:      
                if(environment.Fighter.Breed != 11)
                    CastBoostSpell(environment);

                if (environment.Fighter.Breed == 9)
                    break;
                
                // CastSummonSpell(environment);
                break;
            case AiStep.CastBoost:
                if(environment.Fighter.Breed != 11)
                    CastBoostSpell(environment);
                break;
            case AiStep.CastSpell:
                CastDamageSpell(environment);
                break;
            case AiStep.Move:
            case AiStep.Move2:
                MoveCloser(environment);
                break;
            case AiStep.ReCastAfterMove:
            case AiStep.CastSpell2:
                PlaySpells(environment, IsDamage, 0, true);
                break;
            case AiStep.ReCastAfterMove2:
            case AiStep.ReCastAfterMove3:
                PlaySpells(environment, IsDamage, -1, true);
                break;
        }

        if (!_hasCastedSpell && !_hasMoved)
        {
            if (SetNextStep())
            {       
                if(!NoAnim)
                    await Task.Delay(10, token);
                await PlayerPlay(environment, token);
            }

            //PassTurn();
            return;
        }

    }

    public bool SetNextStep()
    {
        var nextStep = _stepOrder.IndexOf(CurrentStep) + 1;
        if (nextStep >= _stepOrder.Count)
        {
            PassTurn();
            return false;
        }

        _executedSpells = 0;
        CurrentStep = _stepOrder[nextStep];
        return true;
    }

    private void CastDamageSpell(AiEnvironment environment)
    {
        CastDamageSpell(environment, true);
    }

    /// <summary>
    /// Makes the AI-controlled fighter flee away from the target.
    /// </summary>
    /// <param name="environment">The AI environment.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private void FleeAway(AiEnvironment environment)
    {
        Move(environment, true);
    }

    private void MoveCloser(AiEnvironment environment)
    {
        Move(environment, false);
    }

    private void Move(AiEnvironment environment, bool isFleeing, int mp = -1, int recursive = -1)
    {
        var cells = GetReachableCellsByMovement(environment,
                                                mp == -1 ? (int)(environment.Fighter.Stats.Mp.Total * 6) : mp)
            .Where(x => x.Score > -1);

        var cell = !isFleeing ? cells.MinBy(x => x.Score + x.MalusScore) : cells.MaxBy(x => x.Score - x.MalusScore);

        if (cell.ToCellId == 0)
        {
            return;
        }

        var path = cell.GetMovementPath();

        var cellFinal = path.GetFinalCellWithMp((int)environment.Fighter.Stats.Mp.Total + 1);
        var cellFinalHasMark = environment.Fighter.Fight!
                                          .GetMarkInteractingWithCell(cellFinal, true)
                                          .Any(x =>
                                                   x.EndTrigger &&
                                                   x.TeamId != environment.Fighter.Team);

        if (cellFinalHasMark && recursive < 2)
        {
            Log.Information("La cellule finale {CellFinal} est marquée, on réessaye", cellFinal);
            Move(environment, isFleeing, (int)environment.Fighter.Stats.Mp.Total, recursive + 1);
            return;
        }

        var currentScore = GetPositionScore(environment, environment.Fighter.CellId, environment.Fighter.Breed, out _);
        var score = cell.Score;

        if (!isFleeing && score >= currentScore || isFleeing && score <= currentScore)
        {
            return;
        }
        MoveToCell(environment, cell);
    }

    private void CastDamageSpell(AiEnvironment environment, bool damagePass = false)
    {
        var castedDamageSpell = PlaySpells(environment, IsDamage, 0, damagePass);

        if (castedDamageSpell)
        {
            environment.CastedAnyDamageSpell = true;
        }

        if(_hasCastedSpell)
        {
            return;
        }
        
        if (environment.Fighter.IsPlayerBreed())
        {
            PlaySpells(environment, IsDamage, 0, damagePass);
        }
        
        if(_hasCastedSpell)
        {
            return;
        }

        if (environment.Fighter.IsPlayerBreed())
        {
            PlaySpells(environment,
                       IsDamage,
                       (int)Math.Min(environment.Fighter.Stats.Mp.Total, 2),
                       damagePass);
        }
        
        if(_hasCastedSpell)
        {
            return;
        }

        for (var i = 0; i < 3 && environment.CastedAnySpell; i++)
        {
            PlaySpells(environment, IsDamage, -1, damagePass);
            
            if(_hasCastedSpell)
            {
                return;
            }

        }
    }
    
    private void CastSummonSpell(AiEnvironment environment)
    {
        PlaySpells(environment, IsSummonSpell, -1);
    }

    private void CastBoostSpell(AiEnvironment environment)
    {
        PlaySpells(environment, IsStatModifierNoDamage, 0);
    }

    public IEnumerable<T> GetAllFighters<T>(Predicate<T> predicate) where T : FightActor
    {
        return ActorPositions.Values.OfType<T>().Where(entry => predicate(entry));
    }

    /// <summary>
    /// Retrieves the enemies of the AI-controlled fighter.
    /// </summary>
    /// <param name="fighter">The played fighter.</param>
    /// <returns>A list of enemy fighters.</returns>
    private IList<FightActor> GetEnemies(FightActor fighter)
    {
        if (fighter.Fight == null)
            return new List<FightActor>();

        return fighter.Fight.GetAllFighters<FightActor>(x => x.IsAlive() && x.IsEnemyWith(fighter) && !x.IsInvisible)
                      .ToArray();
    }

    /// <summary>
    /// Retrieves the enemies of the AI-controlled fighter.
    /// </summary>
    /// <param name="fighter">The played fighter.</param>
    /// <returns>A list of allies fighters.</returns>
    private IList<FightActor> GetAllies(FightActor fighter)
    {
        if (fighter.Fight == null)
            return new List<FightActor>();

        return fighter.Fight.GetAllFighters<FightActor>(x => x.IsAlive() && x.IsAllyWith(fighter)).ToArray();
    }

    public FightContext GetFightContextSimulation(FighterTranslator fighter)
    {
        var fightersTest = GetFighters(false)
                           .Where(x => !x.IsInvisible() && !x.IsDead)
                           .ToArray();

        var fightContext = new FightContext(10,
                                            new FightSimulation(this),
                                            -1,
                                            fighter,
                                            fightersTest.OfType<FighterTranslator>()
                                                        .Select(x => x.Clone())
                                                        .Cast<HaxeFighter>()
                                                        .ToList())
        {
            IsSimulation = true,
        };

        return fightContext;
    }

    public IList<HaxeFighter> GetFighters(bool allowDead = false)
    {
        return new List<HaxeFighter>(ActorPositions.Values
                                                   .Where(x => allowDead || x.IsAlive())
                                                   .Select(x => x.FighterTranslator));
    }


    private bool PlaySpells(AiEnvironment           environment,
                            Func<SpellLevels, bool> predicate,
                            int                     maxMovementPoints,
                            bool                    damagePass = false)
    {
        try
        {
            if (environment.FightContext.Map.IsFightEnded())
            {
                Log.Information("Le combat est terminé");
                return false;
            }

            if (environment.IsFightEnded || !CanPlayOrPass(environment.Fighter))
            {
                Log.Information("Le combat est terminé ou le joueur ne peut pas jouer");
                return false;
            }

            var results = GetCastPossibleResults(environment,
                                                 predicate,
                                                 maxMovementPoints,
                                                 (int)environment.Fighter.Stats.Ap.Total,
                                                 damagePass);

            if (results.Count == 0)
            {
                Log.Information("Il n'y a pas de résultats");
                return false;
            }

            var maxScore = results.Max(x => x.Score);
            var bestResults = results.Where(x => Math.Abs(x.Score - maxScore) < 0.5).ToArray();


            Log.Information("Il y'a {Results} résultats", bestResults.Length);
            return CastSpells(environment, Random.Shared.GetItems(bestResults, 1)[0]);
        }
        catch (Exception e)
        {
            Log.Error(e, "Erreur lors de la gestion des sorts");
            return false;
        }
    }

    private bool CastSpells(AiEnvironment environment, AiCellResult aiCellResult)
    {
        return MoveToExecuteSpell(environment, aiCellResult.Spell, aiCellResult);
    }

    private bool MoveToExecuteSpell(AiEnvironment environment, SpellWrapper spell, AiCellResult cellResult)
    {
        if (environment.IsFightEnded)
        {
            return false;
        }

        if (!environment.CanMove && cellResult.FromCellId != environment.Fighter.CellId)
        {
            return false;
        }

        if (cellResult.MovementResult != null && environment.Fighter.CellId != cellResult.MovementResult.Value.ToCellId)
        {
            MoveToCell(environment, cellResult.MovementResult.Value);
            return false;
        }

        ExecuteSpell(spell, (short)cellResult.TargetedCell);

        environment.CastedAnySpell = true;
        environment.RefreshContext();

        return true;
    }

    private void ExecuteSpell(SpellWrapper spell, short targetedCell)
    {
        if (_hasCastedSpell)
        {
            return;
        }

        LastCastedSpellId = spell.Id;
        LastCastedSpellLevelId = spell.SpellLevel.Grade;
        
        if(!Client.IsInFight)
            return;
        
        Client.SendRequest(new GameActionFightCastRequest
                                        {
                                            SpellId = spell.Id,
                                            Cell = targetedCell,
                                        },
                                        GameActionFightCastRequest.TypeUrl);
        _executedSpells++;
        _hasCastedSpell = true;
        spell.LastCastTurn = Turn;

        var target = GetFighterAtCell(targetedCell);

        // sois même 
        if (targetedCell == Client.Fighter!.CellId)
        {
            Client.LogInfo($"Lancement du sort {spell.Id} sur sois même", "", "");
        }
        else
        {
            Client.LogInfo($"Lancement du sort {spell.Id} sur la cellule {targetedCell}", "", "");
            // check si on a un ennemi sur la cellule
            if (target != null)
            {
                Client.LogInfo($"Lancement du sort {spell.Id} sur la cellule {targetedCell} sur l'ennemi {target.Id}",
                               "",
                               "");
            }
        }

        // we temporary update our AP 
        // Client.Fighter!.Stats.Ap.Used += (short)spell.GetApCost();


        if (_castedSpells.TryGetValue(spell.Id, out var casted))
        {
            _castedSpells[spell.Id] = casted + 1;
        }
        else
        {
            _castedSpells[spell.Id] = 1;
        }

        if (target != null)
        {
            if (_castedSpellsPerTarget.TryGetValue(spell.Id, out var castedPerTarget))
            {
                _castedSpellsPerTarget[spell.Id] = (target.Id, castedPerTarget.count + 1);
            }
            else
            {
                _castedSpellsPerTarget[spell.Id] = (target.Id, 1);
            }
        }

    }

    private void MoveToCell(AiEnvironment environment, AiMovementCellResult cell)
    {
        if (environment.Fighter.CellId == cell.ToCellId)
        {
            return;
        }

        if (!environment.CanMove)
        {
            return;
        }
        
        if(_hasMoved)
        {
            return;
        }       
        
        Log.Information("La cellulé {ToCellId} est la cellule finale", cell.ToCellId);


        try
        {
            cell.GetMovementPath().CutPath((int)environment.Fighter.Stats.Mp.Total + 1);
            var endCell = cell.GetMovementPath().CellsPath.Last();

            var path = PathFindingClientService.Instance.FindClientPathInFight(Map.Data,
                                                                               (short)environment.Fighter.CellId,
                                                                               (short)endCell.Id,
                                                                               environment.Fighter.Id,
                                                                               this);


            var serverKeys = path.GetServerPath().ToList();

            Client.SendRequest(new MapMovementRequest
                               {
                                   KeyCells = serverKeys.ToList(),
                                   MapId = environment.Fighter.Fight!.Map.Id,
                                   Cautious = false
                               },
                               MapMovementRequest.TypeUrl);

            Client.LogInfo("Déplacement du joueur sur la cellule {EndCellId} depuis {LastCellId} sur la Map " +
                           environment.Fighter.Fight!.Map.Id,
                           endCell.Id.ToString(),
                           environment.Fighter.LastCellId.ToString());

            Client.LogInfo("ServerKeys", "", "");
            _hasMoved = true;

            foreach (var s in serverKeys)
            {
                Client.LogInfo(s.ToString(), "", "");
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "Erreur lors du déplacement du joueur");
        }
    }

    private IList<AiCellResult> GetCastPossibleResults(AiEnvironment environment, Func<SpellLevels, bool> predicate,
                                                       int maxMovementPoints, int actionPoints, bool damagePass)
    {
        if (maxMovementPoints == -1)
        {
            maxMovementPoints = (int)environment.Fighter.Stats.Mp.Total;
        }

        if (!environment.CanMove)
        {
            maxMovementPoints = 0;
        }

        var spells = environment.GetSpells()
                                .Where(x => predicate(x.SpellLevel))
                                .ToArray();

        if (damagePass)
        {
            var spellsOfElements = spells
                                   .Where(x => x.SpellLevel.Effects.Any(y =>
                                                                            y.EffectElement ==
                                                                            environment.ForceElementId ||
                                                                            y.EffectElement == 6))
                                   .ToArray();

            if (environment.ForceElementId != -1 && spellsOfElements.Length >= 1)
            {
                spells = spellsOfElements;
            }
        }

        var results = new List<AiCellResult>();
        foreach (var spell in spells)
        {
            results.AddRange(GetPossibleCells(environment, spell, maxMovementPoints, actionPoints));
        }

        return results;
    }

    private IEnumerable<AiMovementCellResult> GetReachableCellsByMovement(AiEnvironment environment, int mp = -1)
    {
        if (mp == -1)
        {
            mp = (int)environment.Fighter.Stats.Mp.Total;
        }

        var result = new List<AiMovementCellResult>();
        var startCell = environment.Fighter.CellId;
        var movementCells =
            PathFindingClientService.Instance.FindReachableCells(environment.Fighter.Fight!, (short)startCell, mp);

        for (var index = 0; index < movementCells.Length; index++)
        {
            var cell = movementCells[index];

            var outputCellId = (short)environment.Fighter.Fight!.GetOutputPortalCell(cell.CellId);
            var startPoint = MapPoint.GetPoint(startCell);

            if (startPoint == null)
            {
                continue;
            }

            var endPoint = MapPoint.GetPoint(cell.CellId);

            if (endPoint == null)
            {
                continue;
            }

            var cellResult = new AiMovementCellResult
            {
                ToCellId = (short)(outputCellId != -1 ? outputCellId : cell.CellId),
                InputPortalCellId = outputCellId != -1 ? cell.CellId : -1,
                MovementPath = null,
                FromCellId = (short)startCell,
                Fight = environment.Fighter.Fight,
            };

            cellResult.Score = GetPositionScore(environment,
                                                cellResult.ToCellId,
                                                environment.Fighter.Breed,
                                                out var malusScore);
            cellResult.MalusScore += malusScore;

            result.Add(cellResult);
        }

        return result.OrderBy(x => x.Score);
    }

    private double GetPositionScore(AiEnvironment environment, int cell, int breed, out int malusScore)
    {
        malusScore = 0;

        var mapPoint = MapPoint.GetPoint(cell);
        if (mapPoint == null)
        {
            return 0d;
        }


        var marks = environment.Fighter.Fight!
                               .GetMarkInteractingWithCell(cell, true)
                               .Where(x =>
                                          x.EndTrigger &&
                                          x.TeamId != environment.Fighter.Team)
                               .ToList();

        if (marks.Count > 0)
        {
            malusScore = marks.Count * 2;
        }

        var enemyToBlock = environment.Fighter.LastAttacked?.IsAlive() == true
            ? environment.Fighter.LastAttacked
            : environment.Enemies
                         .Where(x => x.IsAlive())
                         .MinBy(x => MapPoint.GetPoint(x.CellId)!.AdjustedManhattanDistance(mapPoint));

        if (enemyToBlock == null)
        {
            return 0;
        }

        return mapPoint.AdjustedManhattanDistance(MapPoint.GetPoint(enemyToBlock.CellId)!) + malusScore;
    }


    private IEnumerable<AiCellResult> GetPossibleCells(AiEnvironment environment,        SpellWrapper spell,
                                                       int?          maxMovement = null, int? maxActionPoints = null)
    {
        environment.SpellsScoreOnCell.Clear();

        var reachedCells = new List<AiCellResult>();
        var fighterStatsMp = (int)environment.Fighter.Stats.Mp.Total;
        var fighterStatsAp = (int)environment.Fighter.Stats.Ap.Total;

        var mp = maxMovement.HasValue ? Math.Min(maxMovement.Value, fighterStatsMp) : fighterStatsMp;
        var movementCells = GetReachableCellsByMovement(environment, mp);
        var currentCell = environment.Fighter.CellId;

        foreach (var movementCell in movementCells)
        {
            var atCell = environment.Fighter.Fight!.Map.Data.GetCell(movementCell.ToCellId);

            if (atCell == null)
                continue;

            var possibleCells = FightLosDetectorService.Instance.GetRangeCells(spell, atCell.Id, true);

            var path = movementCell.GetMovementPath()
                                   .Cells
                                   .Take(movementCell.GetMovementPath().Cells.Length - 1)
                                   .ToArray();

            var tackledMp = path.Select(x => environment.Fighter.GetTackledMp(fighterStatsMp, x)).Sum();
            var tackledAp = path.Select(x => environment.Fighter.GetTackledAp(fighterStatsAp, x)).Sum();

            if (atCell.Id == currentCell)
            {
                tackledAp = 0;
                tackledMp = 0;
            }

            var needTakenCell = spell.GetNeedTakenCell();

            if (!needTakenCell && !IsSummonSpell(spell.SpellLevel))
            {
                if (spell.SpellLevel.Effects.All(x => x.ZoneDescription.Shape == 'P'))
                    needTakenCell = true;
            }

            if (needTakenCell)
            {
                possibleCells = possibleCells.Where(x => environment.Fighter.Fight.GetFighterAtCell(x.Id) != null)
                                             .ToList();
            }
            else if (spell.GetNeedFreeCell())
            {
                possibleCells = possibleCells.Where(x => environment.Fighter.Fight.GetFighterAtCell(x.Id) == null)
                                             .ToList();
            }

            var spellApCost = spell.GetApCost();

            foreach (var cell in possibleCells)
            {
                if (cell.NonWalkableDuringFight)
                {
                    continue;
                }

                var result = new AiCellResult
                {
                    CellId = cell.Id,
                    MpCost = tackledMp + movementCell.GetMovementPath().MpCost,
                    ApCost = tackledAp,
                    FromCellId = movementCell.ToCellId,
                    MovementResult = movementCell,
                    Spell = spell,
                    RemainingMp = fighterStatsMp - tackledMp - movementCell.GetMovementPath().MpCost,
                    RemainingAp = fighterStatsAp - spellApCost - tackledAp,
                };

                if (result.RemainingAp < 0 || (maxActionPoints.HasValue && result.ApCost > maxActionPoints))
                {
                    continue;
                }

                var fixedPortalCell = spell.GetTargetedCell(cell);
                if (fixedPortalCell != null)
                {
                    result.CellId = fixedPortalCell.Value.TargetedCell;
                    result.InputPortalCellId = fixedPortalCell.Value.InputPortalCellId;
                }

                reachedCells.Add(result);
            }
        }

        try
        {
            CalculateScore(environment, reachedCells, spell);
        }
        catch (Exception e)
        {
            Log.Logger.Error(e, "Error in GetPossibleCells");
        }

        //environment.SpellsScoreOnCell.Clear();

        return reachedCells.Where(x => x.Score > 0)
                           .OrderByDescending(x => x.Score);
    }

    private void CalculateScore(AiEnvironment environment, IList<AiCellResult> cells, SpellWrapper spell)
    {
        if (environment.FightContext.Map.IsFightEnded())
        {
            return;
        }

        var zone = spell.GetPreferredPreviewZone();
        var needsFreeCell = spell.GetNeedFreeCell();
        var fight = environment.Fighter.Fight;
        var spellApCost = spell.GetApCost();

        if (!environment.SpellsScoreOnCell.TryGetValue(spell.Id, out var scores))
        {
            scores = new Dictionary<string, SpellScore>();
            environment.SpellsScoreOnCell[spell.Id] = scores;
        }

        var cellsToUse = cells.Where(x => !x.CantBeUsed).ToList();

        if (needsFreeCell)
        {
            // we only take cells that have a fighter on the zone
            if (zone != null && zone is not { Shape: SpellShape.P, })
            {
                foreach (var cell in cells)
                {
                    var cellsInZone = zone.GetCells((uint)cell.TargetedCell);

                    if (cellsInZone.All(x =>
                                            fight!.GetFighterAtCell(x.Id) == null &&
                                            fight.GetFighterAtCell(x.Id) != environment.Fighter))
                    {
                        cellsToUse.Remove(cell);
                    }
                }
            }
        }

        foreach (var cell in cellsToUse)
        {
            var mapCell = environment.Fighter.Fight!.Map.Data.GetCell(cell.TargetedCell)!;

            if (!needsFreeCell)
            {
                if (zone is { Shape: SpellShape.P, } && fight!.GetFighterAtCell(cell.CellId) == null)
                {
                    continue;
                }

                if (zone != null)
                {
                    var zoneCells = zone.GetCells((uint)cell.TargetedCell);
                    if (zoneCells.All(x => fight!.GetFighterAtCell(x.Id) == null))
                    {
                        cell.CantBeUsed = true;
                        continue;
                    }
                }
            }

            var spellCastResult = CanCastSpell(
                environment.Fighter,
                cell.FromCellId,
                spell,
                mapCell,
                spellApCost
            );

            if (spellCastResult == false)
            {
                cell.CantBeUsed = true;
                continue;
            }

            var cellKey = $"{cell.TargetedCell}";

            if (cell.FromCellId == cell.TargetedCell)
            {
                cellKey = $"{cell.TargetedCell}_{cell.FromCellId}";
            }

            if (scores.TryGetValue(cellKey, out var value))
            {
                cell.Score = value.Score;
                cell.DamageOnEnemies = value.DamageOnEnemies;
                cell.DamageOnAllies = value.DamageOnAllies;

                if (cell.Score > 0d)
                {
                    if (cell.Score > 300)
                    {
                        cell.Score += cell.RemainingAp * 10;   
                        cell.Score += cell.RemainingMp * 10;
                    }
                    else
                    {
                        cell.Score += cell.RemainingAp;
                        cell.Score += cell.RemainingMp;
                    }
                }

                continue;
            }

            fight!.SetFightersInitialPositions();
            environment.FightContext.ResetTriggers();
            environment.FightContext.TargetedCell = mapCell.Id;
            environment.FightContext.InputPortalCellId = cell.InputPortalCellId;
            environment.FightContext.IsSimulation = true;

            var currentCell = environment.Fighter.CellId;
            environment.HaxeFighter.SetCurrentPositionCell(cell.FromCellId);
            environment.HaxeFighter.SetBeforeLastSpellPosition(cell.FromCellId);
            environment.HaxeFighter.CarryFighter(null);

            foreach (var fighter in environment.FightContext.Fighters.ToArray())
            {
                fighter.LastRawDamageTaken = null;
                fighter.LastTheoreticalRawDamageTaken = null;
            }

            var affectedFighters = DamageCalculator.DamageComputation(
                environment.FightContext,
                environment.HaxeFighter,
                DamageCalculationTranslator.Instance.CreateSpellFromId(spell.Id, spell.SpellLevel.Grade)!
            );

            environment.FightContext.TempFighters.Clear();

            if (IsSummonSpell(spell.SpellLevel))
            {
                cell.Score = 1000;
            }

            foreach (var fighter in affectedFighters)
            {
                CalculateScore(environment, cell, fighter);

                if (fighter.Breed == 6234 && affectedFighters.Any(y => y.TotalEffects!.Any(z => z.IsSummoning)))
                {
                    cell.Score += 1000;
                }

                fighter.TotalEffects?.Clear();
            }
            environment.HaxeFighter.SetCurrentPositionCell(currentCell);
            environment.HaxeFighter.SetBeforeLastSpellPosition(currentCell);
            environment.HaxeFighter.CarryFighter(null);

            foreach (var fighter in environment.FightContext.Fighters.Where(x => x.IsSimulation).ToArray())
            {
                environment.FightContext.Fighters.Remove(fighter);
                environment.FightContext.TempFighters.Remove(fighter);
            }

            if (!environment.SpellsScoreOnCell.TryGetValue(spell.Id, out scores))
            {
                scores = new Dictionary<string, SpellScore>();
                environment.SpellsScoreOnCell[spell.Id] = scores;
            }

            if (scores.ContainsKey(cellKey))
            {
                continue;
            }

            scores[cellKey] = new SpellScore()
            {
                Score = (int)cell.Score,
                DamageOnAllies = cell.DamageOnAllies,
                DamageOnEnemies = cell.DamageOnEnemies,
            };


            if (cell.Score > 0d)
            {
                cell.Score += (cell.RemainingAp);
                cell.Score += cell.RemainingMp;
            }
        }
    }

    private bool CanCastSpell(FightActor caster, int casterCellId, SpellWrapper castEntity, Cell targetCell, int apCost)
    {
        if (!targetCell.Mov || targetCell.NonWalkableDuringFight || targetCell.FarmCell)
        {
            return false;
        }

        if (apCost > caster.Stats.Ap.Total)
        {
            return false;
        }

        if (castEntity.GetNeedFreeCell() && caster.Fight!.GetFighterAtCell(targetCell.Id) != null)
        {
            return false;
        }

        if (castEntity.GetNeedTakenCell() && caster.Fight!.GetFighterAtCell(targetCell.Id) == null)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(castEntity.SpellLevel.StatesCriterion))
            return false; // we just ignore it idc

        var possibleCells = FightLosDetectorService.Instance.GetRangeCells(castEntity, casterCellId, false);

        if (!possibleCells.Contains(targetCell))
        {
            return false;
        }
        
        var cooldown = castEntity.GetCooldown();
        if (cooldown < 0)
        {
            return false;
        }

        var maxCastPerTurn = castEntity.GetMaxCastPerTurn();

        if (maxCastPerTurn > 0 && _castedSpells.TryGetValue(castEntity.Id, out var casted) && casted >= maxCastPerTurn)
        {
            return false;
        }

        var target = caster.Fight!.GetFighterAtCell(targetCell.Id);
        if (target == null)
        {
            return true;
        }

        var maxCastPerTarget = castEntity.GetMaxCastPerTarget();


        if (maxCastPerTarget > 0 && _castedSpellsPerTarget.TryGetValue(castEntity.Id, out var castedPerTarget))
        {
            if (castedPerTarget.target == target.Id && castedPerTarget.count >= maxCastPerTarget)
            {
                return false;
            }
        }


        return true;
    }


    private void CalculateScore(AiEnvironment environment, AiCellResult cell, HaxeFighter fighter)
    {
        if (fighter.TotalEffects == null)
        {
            return;
        }

        if (fighter.IsDead)
        {
            return;
        }

        var isAlly = fighter.IsAllyWith(environment.Fighter.FighterTranslator);
        var isSelf = fighter.Id == environment.Fighter.FighterTranslator.Id;

        var isSummon = fighter.Data.IsSummon();
        var isMonster = environment.Fighter.Breed > 20;

        if (fighter.TotalEffects.All(x => x.Summon == null) && !fighter.Data.IsAlive())
        {
            return;
        }

        if (cell.Spell.Id == (int)SpellId.PoolayFrit)
        {
            if (fighter.Id == environment.Fighter.Id)
            {
                cell.Score = 9999999;
            }
        }

        var goodEffect = false;
        var effectScore = 0d;

        foreach (var effect in fighter.TotalEffects)
        {
            if (effect.SpellExecutionInfos != null)
            {
                if (environment.Fighter.Breed == 20 && effect.SpellExecutionInfos.Spell.Id == 24302)
                {
                    if (environment.Fighter.Summons.Any(x => x.Breed == 7139))
                    {
                        effectScore += 100000001;
                        break;
                    }
                }

                continue;
            }

            if (fighter.HasState((int)SpellStateId.Zombi) && cell.Spell.Id == (int)SpellId.GeleeBlanche)
            {
                effectScore += 100000000;
                break;
            }

            if (fighter.IsSimulation && isSummon)
            {
                effectScore -= cell.MpCost * 2;
                effectScore += isAlly ? 200 : -200;

                if (isAlly)
                {
                    effectScore += -GetPositionScore(environment, cell.CellId, fighter.Breed, out var malusScore);
                    effectScore -= malusScore;

                    if (isSelf)
                        effectScore += 50;

                    goodEffect = effectScore > 0;
                }

                continue;
            }

            // Disabling Invulnerability has a lot of chance to be a good thing
            if (effect.BuffAdded?.Effect.Param3 == 56)
            {
                effectScore += 100000000;
                continue;
            }

            if (effect.BuffAdded != null && effect.ActionId == ActionId.CharacterShareDamages)
            {
                if (!isSelf)
                {
                    effectScore += isAlly ? 100000001 : -100000001;
                    if (isSelf)
                        effectScore += 50;
                }

                continue;
            }

            if (effect.DamageRange != null)
            {
                effectScore = CalculateDamageScore(cell, fighter, effect, isMonster, effectScore, isAlly);
                goodEffect = effectScore > 0;
                continue;
            }

            if (effect.Dispell)
            {
                effectScore += isAlly ? -100 : 100;
                continue;
            }

            if (effect.ActionId == ActionId.CharacterSetSpellCooldown)
            {
                effectScore += 100;
                continue;
            }

            if (effect.ReduceBuffDuration > 0)
            {
                effectScore += isAlly ? -100 : 100;
                continue;
            }

            if (effect.ApStolen > 0 || effect.AmStolen > 0)
            {
                effectScore += isAlly ? -100 : 100;
                continue;
            }

            if (effect.RangeLoss > 0)
            {
                effectScore += isAlly ? -100 : 100;
                continue;
            }

            if (effect.ApGain > 0 || effect.AmGain > 0)
            {
                effectScore += isAlly ? 100 : -100;
                if (isSelf)
                    effectScore += 50;

                continue;
            }

            if (effect.InvisibilityState != null)
            {
                effectScore += isAlly ? 100 : -100;
                if (isSelf)
                    effectScore += 50;

                continue;
            }

            if (effect.IsKill || effect.Death)
            {
                if (environment.Fighter.Id is (int)MonsterId.Gloutoblop or
                    (int)MonsterId.Gloutovore or
                    (int)MonsterId.GloutovoreAffame)
                {
                    effectScore += 500;
                }
                else
                {
                    effectScore += isAlly ? -500 : 500;
                }

                goodEffect = effectScore > 0;
                continue;
            }

            if (effect.IsPushed)
            {
                effectScore += isAlly ? -100 : 100;
                continue;
            }

            if (effect.MarkTriggered > 0)
            {
                effectScore += isAlly ? -100 : 100;
                continue;
            }

            if (effect.Movement != null)
            {
                var currentPositionScore =
                    -GetPositionScore(environment,
                                      environment.Fighter.CellId,
                                      fighter.Breed,
                                      out var currentMalusScore);
                var positionScore =
                    -GetPositionScore(environment, effect.Movement.NewPosition, fighter.Breed, out var malusScore);

                currentPositionScore -= currentMalusScore;
                positionScore -= malusScore;

                if (isAlly)
                {
                    effectScore = -100;
                }
                else
                {
                    if (positionScore < currentPositionScore)
                    {
                        effectScore += currentPositionScore - positionScore;
                    }
                }

                continue;
            }

            if (effect.MarkAdded != null)
            {
                foreach (var c in effect.MarkAdded.Cells)
                {
                    var f = environment.Fighter.Fight.GetFighterAtCell(c);
                    if (f != null)
                    {
                        if (f.IsAllyWith(environment.Fighter))
                        {
                            effectScore += -500;
                        }
                        else
                        {
                            effectScore += 510;
                        }
                    }
                }

                continue;
            }

            if (effect.BuffAdded != null)
            {
                var actionIdBuff = effect.BuffAdded.Effect.ActionId;

                if (ActionIdHelper.IsHeal(actionIdBuff))
                {
                    effectScore += isAlly
                        ? effect.BuffAdded.Effect.GetMinRoll() * 2
                        : -effect.BuffAdded.Effect.GetMinRoll() * 2;

                    if (isSelf)
                        effectScore += effect.BuffAdded.Effect.GetMinRoll();

                    continue;
                }

                if (ActionIdHelper.IsDamage(EffectRepository.Instance.GetEffectCategory(actionIdBuff), actionIdBuff))
                {
                    effectScore += isAlly
                        ? -effect.BuffAdded.Effect.GetMinRoll() * 2
                        : effect.BuffAdded.Effect.GetMinRoll() * 2;
                    continue;
                }

                if (ActionIdHelper.IsBuff(actionIdBuff) ||
                    actionIdBuff == ActionId.CharacterSpellReflector ||
                    actionIdBuff == ActionId.CharacterLifeLostCasterModerator)
                {
                    effectScore += isAlly ? 20 : -30;
                    if (isSelf)
                        effectScore += 6;

                    continue;
                }

                if (ActionIdHelper.IsDebuff(actionIdBuff))
                {
                    if (environment.Fighter.Breed == (int)MonsterId.TonneauPirate && !isSelf)
                    {
                        effectScore += isAlly ? 500000 : -50000;
                    }

                    effectScore += isAlly ? -60 : 20;
                    continue;
                }

                /*if (!effect.BuffAdded.IsState())
                {
                    effectScore += 1;
                }*/

                continue;
            }

            if (effect.Summon != null)
            {
                effectScore += isAlly ? 100 : -100;
                if (isSelf)
                    effectScore += 4;
            }

            if (effect.ActionId == ActionId.CharacterSummonDeadAllyInFight ||
                effect.ActionId == ActionId.CharacterSummonDeadAllyAsSummonInFight)
            {
                effectScore += isAlly ? 100 : -100;
                if (isSelf)
                    effectScore += 4;
            }

            if (ActionIdHelper.IsBuff(effect.ActionId))
            {
                effectScore += isAlly ? 12 : -10;
                if (isSelf)
                    effectScore += 6;

                continue;
            }

            if (ActionIdHelper.IsDebuff(effect.ActionId))
            {
                effectScore += isAlly ? -50 : 20;
                continue;
            }
        }

        if (isSummon && effectScore > 0)
        {
            effectScore /= 2;
        }

        cell.Score += effectScore;

        if (goodEffect)
        {
            cell.Score += -GetPositionScore(environment, cell.FromCellId, fighter.Breed, out var malusScore);
            cell.Score -= malusScore;
            cell.Score = Math.Max(1, cell.Score);
        }
    }

    private static double CalculateDamageScore(
        AiCellResult cell,      HaxeFighter fighter,     EffectOutput effect,
        bool         isMonster, double      effectScore, bool         isAlly)
    {
        if (effect.DamageRange == null)
        {
            return effectScore;
        }

        var minOrMax = effect.DamageRange.Max == 0 ? effect.DamageRange.Min : effect.DamageRange.Max;

        if (effect.DamageRange.IsHeal)
        {
            return CalculateHealScore(fighter, effect, isMonster, isAlly, minOrMax);
        }

        // Verrouillage
        // With this state the turret can't hit allies
        // disabled in late 2.71 update
        // isSteamerTurret && !fighter.HasState(5279);
        var canHitAllies = false;

        if (!canHitAllies)
        {
            if (!isAlly)
            {
                cell.DamageOnEnemies += minOrMax;
            }
            else
            {
                cell.DamageOnAllies += minOrMax;
            }

            return isAlly ? -minOrMax * 100 : minOrMax * 100;
        }

        // moins il a de vie et plus ça parrait interessant de le taper pour le focus, donc on augmente le score en fonction de la vie restante
        //var lifePercent = fighter.GetLifePoint() / fighter.GetMaxLifeWithoutContext();
        // var lifeScore = 1 - lifePercent;
        // effectScore += lifeScore * 10;

        cell.DamageOnEnemies += minOrMax;
        return minOrMax * 100;
    }

    private static double CalculateHealScore(HaxeFighter fighter, EffectOutput effect, bool isMonster, bool isAlly,
                                             int         minOrMax)
    {
        if (effect.DamageRange == null)
        {
            return 0d;
        }

        if (effect.SourceId == fighter.Id) // this is probably a passive of the character
        {
            // so if the caster is a monster (and stupid) we allow it
            if (isMonster)
            {
                return 0d;
            }
        }

        if (effect.DamageRange.IsShieldDamage)
        {
            return isAlly ? minOrMax * 10 : -minOrMax * 10;
        }

        return isAlly ? minOrMax * 20 : -minOrMax * 20;
    }


    public FightActor? GetFighterAtCell(int cellId)
    {
        var actors = ActorPositions.Values.Where(x => x.CellId == cellId);
        
        return actors.FirstOrDefault(x => !x.IsForcedDead);
    }

    private bool CanPlayOrPass(FightActor fightActor)
    {
        if (!CanPlay(fightActor))
        {
            return false;
        }

        return true;
    }

    private bool CanPlay(FightActor fightActor)
    {
        return fightActor.Stats.Ap.Total > 0;
    }


    public void PassTurn(bool instant = false)
    {
        if (FighterPlaying?.Id != Client.PlayerId)
        {
            return;
        }
        
        _playTokenSource?.Cancel();
        
        if (instant)
        {
            Client.SendRequest(new FightTurnFinishRequest
                               {
                                   IsAfk = false,
                                   IsAfk2 = false
                               },
                               FightTurnFinishRequest.TypeUrl);
            return;
        }
        
        _ = Client.SendRequestWithDelay(new FightTurnFinishRequest
                                        {
                                            IsAfk = false,
                                            IsAfk2 = false
                                        },
                                        FightTurnFinishRequest.TypeUrl,
                                        NoAnim ? 1 : 100, message => !IsEnded && Client.IsInFight);
    }

    public void OnFightIsTurnReadyEvent(FightIsTurnReadyEvent fightIsTurnReadyEvent)
    {
        if (NoAnim)
        {
            Client.SendRequest(new FightTurnReadyRequest
                               {
                                   IsReady = true,
                                   IsReady2 = false
                               },
                                        FightTurnReadyRequest.TypeUrl);
            return;
        }
        
        if (_openedSequence > 0)
        {
            IsMyTurnReady = true;
            return;
        }

        Client.SendRequestWithDelay(new FightTurnReadyRequest
                                    {
                                        IsReady = true,
                                        IsReady2 = true
                                    },
                                    FightTurnReadyRequest.TypeUrl,
                                    NoAnim ? 100 : Random.Shared.Next(100, 200),
                                    (msg) => !IsEnded && Client.IsInFight);
    }

    public void OnSynchronizeEvent(FightSynchronizeEvent fightSynchronizeEvent)
    {
        foreach (var actor in fightSynchronizeEvent.Fighters)
        {
            if (ActorPositions.TryGetValue(actor.ActorId, out var value))
                value.Update(actor);
            else
            {
                AddFightActor(actor);
            }
        }
    }

    public void AddFightActor(ActorPositionInformation actorPositionInformation)
    {
        if (!ActorPositions.TryGetValue(actorPositionInformation.ActorId, out var value))
        {
            if (actorPositionInformation.ActorInformationValue.Fighter.AiFighter != null &&
                actorPositionInformation.ActorInformationValue.Fighter.AiFighter.MonsterFighterInformation != null)
            {
                var monster = MapRepository.Instance.GetMonster((ushort)actorPositionInformation.ActorInformationValue
                                                                    .Fighter.AiFighter.MonsterFighterInformation
                                                                    .MonsterGid);
                if (monster != null)
                {
                    ActorPositions.Add(actorPositionInformation.ActorId, new FightActor(monster, this));
                }
            }
            else
                ActorPositions.Add(actorPositionInformation.ActorId, new FightActor(null, this));
        }

        try
        {
            ActorPositions[actorPositionInformation.ActorId].Update(actorPositionInformation);
        }
        catch (Exception e)
        {
            Log.Logger.Error(e, "Error in AddFightActor");
        }

        if (actorPositionInformation.ActorId == Client.PlayerId)
            FighterPlaying = ActorPositions[actorPositionInformation.ActorId];

        var myself = ActorPositions.GetValueOrDefault(Client.PlayerId);

        if (myself != null)
        {
            var count = 0;
            var countAlly = 0;
            foreach (var fighter in ActorPositions.Values)
            {
                if (BotManager.Instance.IsBotName(fighter.Name) 
                    && Client.PlayerId != fighter.Id 
                    && fighter.Team != myself.Team)
                {
                    count++;
                }
                
                if (BotManager.Instance.IsBotName(fighter.Name) 
                    && fighter.Team == myself.Team)
                {
                    countAlly++;
                }
            }
            
            Client.SetIsAgainstBot(count + " bots");
            Client.SetWithBot(countAlly.ToString());
        }


    }

    public void OnFightFighterShowEvent(FightFighterShowEvent fightFighterShowEvent)
    {
        AddFightActor(fightFighterShowEvent.Information);

        if (!IsInPreparation) return;
        if (Client.Party != null && Client.Party.Members.Count > 1)
        {
            var allReady = 0;
            foreach (var member in Client.Party.Members)
            {
                if (member == Client.PlayerId)
                    continue;

                if (ActorPositions.ContainsKey(member))
                {
                    allReady++;
                }
            }
                    
            if (Client.Trajet != null && allReady == Client.Trajet.MinGroupsPlayers - 1)
            {
                Client.SendRequestWithDelay(new FightReadyRequest
                                            {
                                                IsReady = true
                                            }, FightReadyRequest.TypeUrl,
                                            NoAnim ? 100 : Random.Shared.Next(1000, 2000), (x) => IsInPreparation);
            }
        }
    }

    public void OnFightFighterRefreshEvent(FightFighterRefreshEvent fightFighterRefreshEvent)
    {
        AddFightActor(fightFighterRefreshEvent.Information);
    }

    public void OnFightRefreshCharacterStatsEvent(FightRefreshCharacterStatsEvent fightRefreshCharacterStatsEvent)
    {
        if (ActorPositions.TryGetValue(fightRefreshCharacterStatsEvent.FighterId, out var value))
            value.UpdateStats(fightRefreshCharacterStatsEvent.Stats);
    }

    private bool IsSummonSpell(SpellLevels spellLevel)
    {
        return IsSpell(spellLevel, ActionIdHelper.IsSummon);
    }

    private bool IsDamage(SpellLevels spellLevel)
    {
        return IsSpell(spellLevel,
                       x => ActionIdHelper.IsDamage(EffectRepository.Instance.GetEffectCategory(x), x) ||
                            ActionIdHelper.IsKill(x));
    }

    private bool IsHeal(SpellLevels spellLevel)
    {
        return IsSpell(spellLevel, x => ActionIdHelper.IsHeal(x) || ActionIdHelper.IsShield(x));
    }

    private bool IsMark(SpellLevels spellLevel)
    {
        return IsSpell(spellLevel, ActionIdHelper.IsMark);
    }

    private bool IsMovement(SpellLevels spellLevel)
    {
        return IsSpell(spellLevel, ActionIdHelper.IsTeleport) ||
               IsSpell(spellLevel, ActionIdHelper.IsPush) ||
               IsSpell(spellLevel, ActionIdHelper.IsPull);
    }

    private bool IsStatModifier(SpellLevels spellLevel)
    {
        return IsSpell(spellLevel,
                       x =>
                           spellLevel.SpellId == (int)SpellId.PoolayFrit ||
                           x == ActionId.CharacterBoostDamages ||
                           x == ActionId.CharacterShareDamages ||
                           x == ActionId.CharacterSpellReflector ||
                           x == ActionId.CharacterLifeLostCasterModerator ||
                           ActionIdHelper.IsStatModifier(x) &&
                           !ActionIdHelper.IsDebuff(x) &&
                           x != ActionId.CharacterBoostMovementPoints);
    }

    private bool IsStatModifierNoDamage(SpellLevels spellLevel)
    {
        if (IsDamage(spellLevel))
            return false;

        return IsStatModifier(spellLevel);
    }

    private bool IsMovementPointBoost(SpellLevels spellLevel)
    {
        return IsSpell(spellLevel, x => x == ActionId.CharacterBoostMovementPoints);
    }


    /// <summary>
    /// Determines if a given spell matches a specific criterion.
    /// </summary>
    /// <param name="spellLevel">The level of the spell to be checked.</param>
    /// <param name="predicate">A function that checks if a given action ID matches a criterion.</param>
    /// <returns>True if the spell matches the criterion, false otherwise.</returns>
    private bool IsSpell(SpellLevels spellLevel, Func<ActionId, bool> predicate)
    {
        var maxIteration = 100;

        var result = false;
        var effects = spellLevel.Effects.Where(x => !x.ForClientOnly).ToList();

        // Maitrise des invocations et Liberté des invocations
        if (spellLevel.SpellId is 18646 or 22312)
        {
            return false;
        }

        while (effects.Count > 0 && maxIteration > 0)
        {
            var effect = effects[0];

            if (ActionIdHelper.IsSpellExecution((ActionId)effect.EffectId))
            {
                var spellExecute = SpellRepository.Instance.GetSpellLevel((short)effect.DiceNum, (byte)effect.DiceSide);
                if (spellExecute != null)
                {
                    effects.AddRange(spellExecute.Effects.Where(x => !x.ForClientOnly).ToList());
                }
            }

            if (predicate((ActionId)effect.EffectId))
            {
                result = true;
                break;
            }

            maxIteration--;
            effects.RemoveAt(0);
        }

        return result;
    }

    public bool HasEntity(short cellId, bool b)
    {
        return ActorPositions.Values.Any(x => x.CellId == cellId && x.IsAlive());
    }

    public bool PointLos(short cellId)
    {
        return Map.Data.GetCell(cellId)?.Los ?? false;
    }

    public HaxeFighter? GetLastKilledAlly(int teamId)
    {
        return null;
    }

    public void SetFightersInitialPositions()
    {
        foreach (var fighter in ActorPositions.Values)
        {
            fighter.BeforeLastSpellPosition = fighter.CellId;
            fighter.FighterTranslator.BeforeLastSpellPosition = fighter.CellId;
        }

        FightersInitialPositions = ActorPositions.Values
                                                 .Where(x => x.IsAlive())
                                                 .Select(x => new FighterCellLight(x.Id, (short)x.CellId))
                                                 .ToList();
    }

    public List<FighterCellLight> GetFightersInitialPositions()
    {
        return FightersInitialPositions;
    }

    public HaxeFighter? GetFighterById(long fighterId)
    {
        return ActorPositions.TryGetValue(fighterId, out var value) ? value.FighterTranslator : null;
    }

    public IList<long> GetEveryFighterId()
    {
        return ActorPositions.Keys.ToList();
    }

    public long GetCarriedFighterIdBy(HaxeFighter fighter)
    {
        return ActorPositions.Values.FirstOrDefault(x => x.CarriedFighterId == fighter.Id)?.Id ?? -1;
    }

    public int GetFreeId()
    {
        // Return the first positive identifier that is not already used.
        // The previous loop incremented even when the identifier was free
        // and never terminated because its success condition was not reached.
        var newId = 1;
        while (ActorPositions.ContainsKey(newId))
        {
            newId++;
        }

        return newId;
    }

    public int GetOutputPortalCell(int cellId)
    {
        var mark = GetMarkInteractingWithCell(cellId, true, GameActionMarkType.Portal);

        if (mark.Count == 0)
        {
            return MapTools.InvalidCellId;
        }

        var entryPortal = mark[0];

        var marks = GetMarkInteractingWithCell(entryPortal.MainCell, true, GameActionMarkType.Portal);

        if (!marks.Any())
        {
            return MapTools.InvalidCellId;
        }

        var portals = GetMarks(true, GameActionMarkType.Portal, (int)entryPortal.TeamId);

        var usedPortals = PortalUtils.GetPortalChainFromPortals(entryPortal, portals.Where(x => !x.Used).ToArray());
        usedPortals.Reverse();

        return usedPortals.Count > 0 ? usedPortals[0].MainCell : MapTools.InvalidCellId;
    }

    public IList<Mark> GetMarks(bool activeOnly, GameActionMarkType? markType = null, int? teamId = null)
    {
        return _marks.Where(x =>
                                (!activeOnly || x.Active) &&
                                (!markType.HasValue || x.MarkType == markType.Value) &&
                                (!teamId.HasValue || x.TeamId == teamId.Value)).ToList();
    }

    public IList<Mark> GetMarkInteractingWithCell(int cellId, bool activeOnly, GameActionMarkType? markType = null)
    {
        return _marks.Where(x =>
                                (!activeOnly || x.Active) &&
                                (!markType.HasValue || x.MarkType == markType.Value) &&
                                x.Cells.Any(y => y == cellId)).ToList();
    }

    public IList<Mark> GetMarkInteractingWithCell(Mark[]              marks, int cellId, bool activeOnly,
                                                  GameActionMarkType? markType = null)
    {
        return marks.Where(x =>
                               !x.IsDeleted &&
                               (!activeOnly || x.Active) &&
                               (!markType.HasValue || x.MarkType == markType.Value) &&
                               x.Cells.Any(y => y == cellId)).ToList();
    }

    public bool IsFightEnded()
    {
        return IsEnded;
    }

    public bool IsCellFree(int newLocation)
    {
        return !ActorPositions.Values.Any(x => x.CellId == newLocation && x.IsAlive()) &&
               Map.Data.IsCellWalkableFight(newLocation);
    }

    public int GetOutputPortals(Mark mark, out IList<Mark> usedPortals)
    {
        var portals = GetMarks(true, GameActionMarkType.Portal, (int)mark.TeamId);
        usedPortals = PortalUtils.GetPortalChainFromPortals(mark, portals.Where(x => !x.Used).ToArray());
        usedPortals.Insert(0, mark);

        return usedPortals.Count > 0 ? usedPortals[^1].MainCell : MapTools.InvalidCellId;
    }

    public void OnErrorCast()
    {
        if (FighterPlaying?.Id == Client.PlayerId)
        {
            _illegalActions++;

            if (_illegalActions >= 3)
            {
                _playTokenSource.Cancel();
                return;
            }
        }

        if (FighterPlaying?.Id == Client.PlayerId && _openedSequence == 0)
        {
            SetNextStep();
            PlayStep(false, _playTokenSource.Token);
        }
    }

    public void OnMapMovementRefused(MapMovementRefusedEvent mapMovementRefusedEvent)
    {
        // we check my fighter
        var endCell = MapTools.GetCellIdByCoord(mapMovementRefusedEvent.CellX, mapMovementRefusedEvent.CellY);
        Client.LogInfo("MapMovementRefusedEvent, {endCell}: ", endCell, "");
        
        if (Client.Fighter != null)
            Client.Fighter.CellId = endCell;
        
        if (FighterPlaying?.Id == Client.PlayerId)
        {
            _illegalActions++;

            if (_illegalActions >= 3)
            {
                _playTokenSource.Cancel();
                return;
            }
        }

        if (FighterPlaying?.Id == Client.PlayerId && (CurrentStep == AiStep.Move || CurrentStep == AiStep.Move2))
        {
            SetNextStep();
            PlayStep(false, _playTokenSource.Token);
        }
    }
    
    public bool SpectateState { get; private set; }

    public void SetSpectateSecret(bool state)
    {
        SpectateState = state;
    }
}
