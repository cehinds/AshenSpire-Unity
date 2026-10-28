// Focused checks of phases 9 and 10 the JS oracle families do not reach: the combat lock on every edit door, the deck
// editor's refusals and Cancel, "Add to deck" refusals, Armoury refusal codes and gear grants, the Armoury glow, the spoils
// stages (receipt, notices, offer; a notice-only spoils ending the run), progression settings, the new save fields, and
// the profile-level unlock save with its refusals.
using AshenSpire.Domain.Core2;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

static class Core2ProgressChecks
{
    public static void Run(Core2Content content, Core2RunData data, Action<bool, string> check, Action<object, object, string> equal, Func<Action, bool> throws)
    {
        // ---- the combat lock -------------------------------------------------------------------------------------------
        var session = Core2RunSession.Start(data, "reaver", 41);
        check(!session.EditingLocked, "lock: the map is not locked");
        session.Travel(session.TravelOptions()[0]);
        equal(session.Screen, Core2Screen.Fight, "lock: a floor 1 node opens a fight");
        check(session.EditingLocked, "lock: an unresolved fight locks deck and equipment edits (editingLocked)");
        check(throws(() => session.OpenDeckEdit()), "lock: the deck editor is refused during a fight");
        check(throws(() => session.OpenArmoury()), "lock: the Armoury is refused during a fight");
        equal(session.Equip("rightHand", 0, null)?.Code, "locked", "lock: an Unequip during a fight is refused as locked");
        equal(session.Equip("rightHand", 1, session.Run.Loadout.Equipped("rightHand"))?.Code, "locked", "lock: a Replace during a fight is refused as locked");
        session.SettleFightAsWon();
        check(!session.EditingLocked, "lock: a resolved fight unlocks edits (the spoils may open the editor)");

        // An editor opened on the map is a modal: travel into a fight waits until it closes.
        var modal = Core2RunSession.Start(data, "reaver", 43);
        var openEdit = modal.OpenDeckEdit();
        check(throws(() => modal.Travel(modal.TravelOptions()[0])), "lock: travel is refused while a deck edit is open");
        equal(modal.Screen, Core2Screen.Map, "lock: a refused travel leaves the run on the map");
        openEdit.Cancel();
        modal.Travel(modal.TravelOptions()[0]);
        equal(modal.Screen, Core2Screen.Fight, "lock: travel proceeds once the edit is closed");

        // Play in deck order (JS createCombat orderedDraw): the fight draws the run's deck in order, Innate cards first.
        var ordered = Core2RunSession.Start(data, "reaver", 43);
        ordered.DeckSettings = Core2DeckSettings.From(data, new JObject { ["playInDeckOrder"] = true });
        var runOrder = ordered.Run.Deck.Select(c => c.InstanceId).ToList();
        ordered.Travel(ordered.TravelOptions()[0]);
        var drawn = ordered.Fight.Hand.Concat(ordered.Fight.Draw).Select(c => c.InstanceId).ToList();
        bool Innate(string id) => Core2Json.Strings(content.Card(ordered.Run.Deck.First(c => c.InstanceId == id).CardId).Raw["keywords"]).Contains("innate");
        check(drawn.SequenceEqual(runOrder.Where(Innate).Concat(runOrder.Where(id => !Innate(id)))), "deck order: under Play in deck order the fight draws the run's deck order");

        // ---- the spoils stages ---------------------------------------------------------------------------------------
        equal(session.RewardStage, "victoryReceipt", "spoils: the victory receipt shows first");
        var receipt = session.Run.PendingReward.VictoryReceipt;
        equal((string)receipt["title"], "VICTORY", "spoils: a normal fight's receipt is headed VICTORY");
        equal((int?)receipt["xpGains"]["tracks"]["class:reaver"], 5, "spoils: the receipt's XP lines carry the class award (perWin 5)");
        check(receipt["xpBefore"]["character"] != null && receipt["xpBefore"]["tracks"] is JObject, "spoils: the receipt keeps the ledgers before the fight");
        check(throws(() => session.TakeReward(0)), "spoils: no offer can be taken before the receipt is acknowledged");
        check(throws(() => session.ContinueReward()), "spoils: Continue waits for the receipt");
        check(throws(() => session.AcknowledgeLevelNotices()), "spoils: notices cannot be acknowledged out of order");
        var saved = Core2RunSave.Deserialize(content, Core2RunSave.Serialize(content, session.Run));
        check(saved.PendingReward.VictoryReceipt != null, "spoils: an unacknowledged receipt survives a reload (it is shown, never paid, again)");
        session.AcknowledgeVictoryReceipt();
        equal(session.RewardStage, "offer", "spoils: the offer follows (no level crossed)");
        check(throws(() => session.AcknowledgeVictoryReceipt()), "spoils: a receipt is acknowledged once");

        // ---- deck editor ----------------------------------------------------------------------------------------------
        var edit = session.OpenDeckEdit();
        check(throws(() => session.OpenDeckEdit()), "deck: one editor session at a time");
        check(throws(() => session.TakeRewardToDeck(0)) && throws(() => session.TakeReward(0)) && throws(() => session.ContinueReward()),
            "deck: the spoils wait while a deck edit is open (Cancel would erase a claimed reward)");
        check(throws(() => session.Equip("rightHand", 0, null)), "deck: the Armoury waits while a deck edit is open");
        var deckBefore = string.Join(",", session.Run.Deck.Select(c => c.InstanceId));
        edit.Add("basic:core.strike");
        edit.Remove(session.Run.Deck[0].InstanceId);
        check(edit.CanUndo, "deck: a successful edit can be undone");
        edit.Cancel();
        equal(string.Join(",", session.Run.Deck.Select(c => c.InstanceId)), deckBefore, "deck: Cancel restores the deck exactly");
        equal(session.Run.EditMintCounter, null, "deck: Cancel restores the mint counter (no mint number is spent)");
        check(throws(() => edit.Add("basic:core.strike")), "deck: a closed session refuses further edits");
        edit = session.OpenDeckEdit();
        foreach (var card in session.Run.Deck.Take(3).ToList()) edit.Remove(card.InstanceId);
        var below = edit.Confirm();
        check(!below.Ok && below.Refusal == "Your deck has 7 cards; it needs at least 10.", "deck: Confirm is refused below the minimum, by name (" + below.Refusal + ")");
        check(!edit.Closed, "deck: a refused Confirm keeps the session open");
        check(throws(() => edit.Add("basic:core.reaverSpecial")), "deck: a card the Adept training does not cover cannot be minted");
        check(throws(() => edit.Add("bogus:key")), "deck: an unknown tile key is refused");
        equal(edit.Add("card:core.reaverSpecial").Refusal, "allInDeck", "deck: a limited card with no loose copy is refused (all in the deck)");
        edit.Add("basic:core.strike"); edit.Add("basic:core.strike"); edit.Add("basic:core.defend");
        check(edit.Confirm().Ok && edit.Closed, "deck: Confirm succeeds inside the bounds and closes the session");
        check(session.Run.Deck.Any(c => c.InstanceId == "edit:1") && session.Run.EditMintCounter == 3, "deck: minted Adept copies take edit:N ids and the counter persists");
        var mintSave = Core2RunSave.Deserialize(content, Core2RunSave.Serialize(content, session.Run));
        equal(mintSave.EditMintCounter, 3, "save: the mint counter round-trips");
        session.DeckSettings = Core2DeckSettings.From(data, new JObject { ["deckEditing"] = false });
        check(throws(() => session.OpenDeckEdit()), "deck: with Deck editing off the Armoury opens no editor");
        session.DeckSettings = Core2DeckSettings.From(data, new JObject { ["deckMaxUnlimited"] = false, ["deckMaxSize"] = 5 });
        check(Core2RunDeck.Bounds(session.DeckSettings).Problem.StartsWith("Maximum deck size (5) is below the minimum (10)"), "deck: a maximum below the minimum is refused by name");
        session.DeckSettings = Core2DeckSettings.From(data);
        check(Core2RunDeck.Tiles(data, session.Run).Where(t => t.Unlimited).Select(t => t.CardId).SequenceEqual(new[] { "core.strike", "core.defend", "core.counter" }),
            "deck: a Reaver's Martial Adept mints are its collected Basics (Strike, Defend, Counter)");
        var starseer = Core2RunSession.Start(data, "starseer", 41, ignoreUnlocks: true);
        starseer.Run.Deck.RemoveAll(c => c.CardId == "core.cantrip"); starseer.Run.CollectedCardIds.Remove("core.cantrip");
        equal(Core2RunDeck.UnlimitedCardIds(data, starseer.Run).Count, 0, "deck: a Magic Adept mints nothing until a Cantrip is collected");
        starseer.Run.CollectedCardIds.Add("core.cantrip");
        check(Core2RunDeck.UnlimitedCardIds(data, starseer.Run).SequenceEqual(new[] { "core.cantrip" }), "deck: a collected Cantrip becomes the Magic Adept's unlimited card");
        equal((string)Core2RunDeck.AdeptTraining(data, starseer.Run)["label"], "Magic Adept", "deck: the Starseer trains as Magic Adept");

        // ---- a won card straight into the deck ------------------------------------------------------------------------
        var offer = session.CurrentOffer;
        for (var i = 0; i < offer.Options.Count; i++)
        {
            var kind = offer.Options[i].Kind;
            var code = session.RewardDeckAddRefusal(i);
            check(kind == "combatCards" || kind == "weaponArts" ? code == "" : code == "notCard", "reward: Add to deck answers '" + code + "' for a " + kind + " option");
        }
        session.DeckSettings = Core2DeckSettings.From(data, new JObject { ["deckMaxUnlimited"] = false, ["deckMaxSize"] = session.Run.Deck.Count });
        var cardIndex = offer.Options.FindIndex(o => o.Kind == "combatCards" || o.Kind == "weaponArts");
        if (cardIndex >= 0)
        {
            equal(session.RewardDeckAddRefusal(cardIndex), "full", "reward: Add to deck is refused when the deck is at its maximum");
            check(throws(() => session.TakeRewardToDeck(cardIndex)) && !offer.Taken.Contains(cardIndex), "reward: a refused Add to deck claims nothing");
            session.DeckSettings = Core2DeckSettings.From(data);
            var count = session.Run.Deck.Count;
            session.TakeRewardToDeck(cardIndex);
            check(session.Run.Deck.Count == count + 1 && session.Run.Deck.Last().InstanceId.StartsWith("reward:"), "reward: Add to deck claims the card into the deck");
            session.TakeRewardToDeck(cardIndex);
            equal(session.Run.Deck.Count, count + 1, "reward: a repeated Add to deck changes nothing");
        }
        session.DeckSettings = Core2DeckSettings.From(data);
        session.ContinueReward();

        // ---- Armoury: glow, refusals, gear grants -----------------------------------------------------------------------
        var armoury = Core2RunSession.Start(data, "reaver", 43);
        check(!armoury.ArmouryHasNew, "armoury: a new run's gear is all seen");
        armoury.OpenArmoury();
        armoury.Run.Loadout.Storage.Add("boneSceptre");
        check(armoury.ArmouryHasNew && Core2RunArmoury.NewItems(armoury.Run).SequenceEqual(new[] { "boneSceptre" }), "armoury: a found armament makes the Armoury glow");
        var seenSave = Core2RunSave.Deserialize(content, Core2RunSave.Serialize(content, armoury.Run));
        check(seenSave.ArmorySeen != null && !seenSave.ArmorySeen.Contains("boneSceptre"), "save: the Armoury's seen list round-trips");
        var positions = armoury.OpenArmoury();
        check(!armoury.ArmouryHasNew, "armoury: looking clears the glow");
        var weapon = positions.First(p => p.SlotId == "rightHand" && p.SetIndex == 0);
        check(weapon.ItemId == armoury.Run.Loadout.Equipped("rightHand") && weapon.Values.Count > 0 && weapon.Active, "armoury: Inspect shows the worn weapon's resolved values");
        check(armoury.ReplaceCandidates("rightHand").Contains("boneSceptre") && !armoury.ReplaceCandidates("rightHand").Contains("straightSword"), "armoury: the picker offers carried pieces only");
        var replaced = armoury.Run.Loadout.Equipped("rightHand");
        check(armoury.Equip("rightHand", 0, "boneSceptre") == null, "armoury: Replace puts a carried piece in the cell");
        check(armoury.Run.Loadout.Storage.Contains(replaced) && !armoury.Run.Loadout.Storage.Contains("boneSceptre"), "armoury: the replaced weapon goes to storage");
        check(armoury.Run.Deck.Any(c => c.GrantedBy == "boneSceptre" && c.CardId == "core.manaSurge"), "armoury: the Bone Sceptre's Mana Surge is lent to the deck");
        equal(armoury.Equip("rightHand", 0, "straightSword")?.Code, "unowned", "armoury: a piece the run does not carry is refused");
        equal(armoury.Equip("armor", 0, "boneSceptre")?.Code, "slot", "armoury: a weapon does not fit the armour slot");
        equal(armoury.Equip("rightHand", 7, "boneSceptre")?.Code, "location", "armoury: a cell that does not exist is refused");
        var locked = data.Content.Armour.Select(a => a.Raw).First(a => (string)a["classId"] == "reaver" && !string.IsNullOrEmpty((string)a["unlock"]));
        equal(armoury.Equip("armor", 0, (string)locked["id"])?.Code, "unowned", "armoury: armour still locked behind an unlock is refused");
        check(armoury.Unequip("rightHand", 0) == null && !armoury.Run.Deck.Any(c => c.GrantedBy == "boneSceptre"), "armoury: Unequip takes the lent card back");
        armoury.Equip("rightHand", 0, armoury.Run.Loadout.Storage[0]);
        foreach (var id in data.Content.Armaments.Select(a => a.Id))
        {
            if (armoury.Run.Loadout.Storage.Count >= 8) break;
            if (!armoury.Run.Loadout.Storage.Contains(id) && !armoury.Run.Loadout.Sets.Values.SelectMany(v => v).Contains(id)) armoury.Run.Loadout.Storage.Add(id);
        }
        var full = armoury.Unequip("rightHand", 0);
        check(full?.Code == "storage" && full.Text.StartsWith("Inventory is full (8/8)"), "armoury: Unequip is refused when storage is full (" + full + ")");

        // ---- progression settings: multipliers and character levels -----------------------------------------------------
        var levels = Core2RunSession.Start(data, "reaver", 44);
        levels.Rules = new Core2ProgressionRules { CharacterLevels = false, ClassXpMultiplier = 2, WeaponXpMultiplier = 1 };
        levels.Travel(levels.TravelOptions()[0]);
        levels.SettleFightAsWon();
        equal(levels.Run.Level.Xp, 0, "levels: with character levels off no character XP is banked");
        equal(levels.Run.Skills["class:reaver"].Xp, 10, "levels: the class XP multiplier doubles a win's pay (5 × 2)");
        levels.Run.Skills["class:reaver"].Xp = 398;
        levels.AcknowledgeVictoryReceipt();
        levels.ContinueReward();
        levels.Travel(levels.TravelOptions()[0]);
        equal(levels.Screen, Core2Screen.Fight, "notices: seed 44's second node is a fight");
        if (levels.Screen == Core2Screen.Fight)
        {
            levels.SettleFightAsWon();
            levels.AcknowledgeVictoryReceipt();
            equal(levels.RewardStage, "levelNotices", "notices: a class level crossing shows its notice after the receipt");
            var notice = (JObject)levels.Run.PendingReward.Progression[0];
            check((string)notice["skillId"] == "class:reaver" && (int)notice["after"] == 1 && (int?)notice["effects"]["maxLevel"] == 20, "notices: the notice names the track, the new level and the cap");
            levels.AcknowledgeLevelNotices();
            equal(levels.RewardStage, "offer", "notices: the offer follows the acknowledged notices");
        }
        check(data.Content.Cards.All(c => !c.Tags.Any(t => Core2Json.Strings(data.Skill["pieceSchools"]).Contains(t))),
            "notices: no Core 2 card carries a held piece's card school, so no level changes a card number (the notices' empty change rows)");

        // ---- a notice-only spoils ends the run once acknowledged ---------------------------------------------------------
        var ending = Core2RunSession.Start(data, "reaver", 45).Run;
        ending.PendingReward = new Core2PendingReward { OfferId = "3:n9_0:combat", Source = "combat", After = "victory", NoticeOnly = true,
            VictoryReceipt = new JObject { ["title"] = "THE KING FALLS", ["xpBefore"] = new JObject(), ["xpGains"] = new JObject { ["level"] = 0, ["tracks"] = new JObject() } },
            Progression = new JArray(new JObject { ["skillId"] = "item:blade", ["label"] = "Blade", ["before"] = 1, ["after"] = 2 }) };
        var resumed = Core2RunSession.Resume(data, Core2RunSave.Deserialize(content, Core2RunSave.Serialize(content, ending)));
        equal(resumed.RewardStage, "victoryReceipt", "notice-only: a reloaded final spoils resumes on its receipt");
        check(resumed.CurrentOffer == null && throws(() => resumed.TakeReward(0)), "notice-only: the final spoils has no offer");
        resumed.AcknowledgeVictoryReceipt();
        equal(resumed.Screen, Core2Screen.Reward, "notice-only: the notices still wait");
        equal(resumed.AcknowledgeLevelNotices(), Core2Screen.Finished, "notice-only: acknowledging the last notice finishes the run");
        check(resumed.Run.Finish?.Victory == true && resumed.Run.PendingReward == null, "notice-only: the run finished as a victory");
        var lost = Core2RunSession.Start(data, "rogue", 46, ignoreUnlocks: true).Run;
        lost.Hp = 0;
        lost.PendingReward = new Core2PendingReward { OfferId = "1:n2_0:combat", Source = "combat", After = "defeat", NoticeOnly = true,
            Progression = new JArray(new JObject { ["skillId"] = "item:blade", ["label"] = "Blade", ["before"] = 0, ["after"] = 1 }) };
        var defeat = Core2RunSession.Resume(data, lost);
        equal(defeat.RewardStage, "levelNotices", "notice-only: a defeat with a level crossing shows the notice");
        equal(defeat.AcknowledgeLevelNotices(), Core2Screen.Finished, "notice-only: then the run ends");
        check(defeat.Run.Finish?.Victory == false, "notice-only: as a defeat");

        // ---- grandfathered rest points ----------------------------------------------------------------------------------
        var rest = Core2RunSession.Start(data, "reaver", 47).Run;
        var shrine = rest.MapGraph.NodeList.First(n => n.Type == "shrine");
        rest.MapNodeId = shrine.Id;
        Core2RunRest.Begin(data, rest);
        var refusal = "";
        try { Core2RunRest.Commit(data, rest, "levelUp", attributeId: "strength", characterLevels: false); } catch (InvalidOperationException error) { refusal = error.Message; }
        equal(refusal, "Character leveling is disabled for this profile", "levels: Level Up is refused when levels are off and no point waits");
        rest.Level.UnspentPoints = 1;
        Core2RunRest.Commit(data, rest, "levelUp", attributeId: "strength", characterLevels: false);
        equal(rest.Level.UnspentPoints, 0, "levels: an earned point stays claimable with levels off (grandfathered)");

        // ---- save fields --------------------------------------------------------------------------------------------------
        var skilled = Core2RunSession.Start(data, "starseer", 48, ignoreUnlocks: true).Run;
        Core2RunSkills.Award(data, skilled, "item:magic-focus", 30);
        var skilledSave = Core2RunSave.Deserialize(content, Core2RunSave.Serialize(content, skilled));
        check(skilledSave.Skills.TryGetValue("item:magic-focus", out var focus) && focus.Level == 1 && focus.Xp == 30 && focus.PendingDrafts == 1, "save: the skill ledger round-trips (Spellcraft opened at level 1)");

        // ---- the profile-level unlock save --------------------------------------------------------------------------------
        var profile = new Core2Profile();
        var ledger = profile.Ledger(content);
        equal(Core2Profile.ProfileId(content), "core", "profile: the ledger is keyed by the game profile's id");
        check(!Core2RunProgression.ClassAvailable(content, ledger, "starseer"), "profile: a fresh profile has only Reaver");
        Core2RunProgression.RecordFinish(content, ledger, "run-1", false);
        var text = Core2ProfileSave.Serialize(profile);
        var loaded = Core2ProfileSave.Deserialize(text);
        check(Core2RunProgression.ClassAvailable(content, loaded.Ledger(content), "starseer"), "profile: an unlock survives the profile save");
        check(!text.Contains("\"run\""), "profile: the profile save carries no run");
        string Refused(string save) { try { Core2ProfileSave.Deserialize(save); return "loaded"; } catch (Core2SaveRefused error) { return error.Reason; } }
        string Edit(Action<JObject> change) { var row = JObject.Parse(text); change(row); return row.ToString(Formatting.None); }
        equal(Refused("{not json"), "unreadable", "profile save: unreadable text is refused");
        equal(Refused(Edit(r => r["format"] = Core2RunSave.Format)), "format", "profile save: a run save is not a profile");
        equal(Refused(Edit(r => r["formatVersion"] = Core2ProfileSave.FormatVersion + 1)), "newer", "profile save: a newer format version is refused");
        equal(Refused(Edit(r => r.Remove("formatVersion"))), "format", "profile save: a save without a version is refused");
        equal(Refused(Edit(r => r["gameProfileUnlocks"]["core"]["completedRunIds"] = new JArray("a", "a"))), "unreadable", "profile save: a ledger with a repeated run id is refused");
        equal(Refused(Edit(r => r.Remove("gameProfileUnlocks"))), "unreadable", "profile save: a save without a ledger is refused");
        try { Core2RunSave.Deserialize(content, text); check(false, "profile save: a run load refuses a profile save"); }
        catch (Core2SaveRefused error) { equal(error.Reason, "format", "profile save: a run load refuses a profile save"); }
        var other = Core2ProfileSave.Deserialize(Edit(r => r["gameProfileUnlocks"]["legacy"] = new JObject { ["completedRunIds"] = new JArray("x") }));
        check(other.Unlocks.ContainsKey("legacy") && other.Ledger(content).CompletedRunIds.SequenceEqual(new[] { "run-1" }), "profile save: another profile's ledger is kept apart");
    }
}
