// Program.cs — Лаба 1: Археологическая экспедиция
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Lab1.ArchaeologySim
{
    #region Enums
    public enum WeatherState { Clear, Windy, Sandstorm }
    public enum Role { Archaeologist, Engineer, Guide }
    public enum DroneStatus { Idle, Surveying, Broken }
    public enum ArtifactType { Pottery, Tool, Tablet, Jewelry }
    #endregion

    #region Records (требуют IsExternalInit.cs в проекте)
    public record Coordinates(int X, int Y);
    public record LogEntry(DateTime Time, string Message);
    #endregion

    #region Interfaces
    public interface ILogger { void Log(LogEntry entry); }

    public interface IInventoryItem
    {
        string Title { get; }
        double Weight { get; }
        void Use();
    }

    public interface IRepairable
    {
        bool IsBroken { get; }
        void Repair();
    }
    #endregion

    #region Exceptions
    [Serializable]
    public class DomainCheckedException : Exception
    {
        private readonly string _msg;
        public DomainCheckedException(string message) { _msg = message; }
        public override string Message { get { return _msg; } }
    }

    [Serializable]
    public class NotEnoughSuppliesException : DomainCheckedException
    {
        public NotEnoughSuppliesException(string what, int need, int have)
            : base("Недостаточно ресурсов для операции: " + what + ". Требуется " + need + ", доступно " + have + ".")
        { }
    }

    [Serializable]
    public class ToolBrokenException : DomainCheckedException
    {
        public ToolBrokenException(string tool) : base("Инструмент «" + tool + "» сломан и требует ремонта/замены.") { }
    }
    #endregion

    #region Logging
    public sealed class ConsoleLogger : ILogger
    {
        public void Log(LogEntry entry)
        {
            Console.WriteLine("[" + entry.Time.ToString("HH:mm:ss") + "] " + entry.Message);
        }
    }
    #endregion

    #region Core Domain
    public abstract class Entity : IEquatable<Entity>
    {
        public Guid Id { get; private set; }
        public string Name { get; private set; }

        protected Entity(string name)
        {
            Id = Guid.NewGuid();
            Name = name ?? "Unnamed";
        }

        protected string ShortId()
        {
            var s = Id.ToString();
            return s.Substring(0, Math.Min(8, s.Length));
        }

        public override string ToString() { return GetType().Name + " «" + Name + "» (" + ShortId() + ")"; }

        public override bool Equals(object obj) { return Equals(obj as Entity); }

        public bool Equals(Entity other) { return other != null && other.Id == Id; }

        public override int GetHashCode() { return Id.GetHashCode(); }

        public abstract void Act(World world);
    }

    public abstract class Character : Entity
    {
        public Role Role { get; private set; }
        protected Character(string name, Role role) : base(name) { Role = role; }

        public sealed override string ToString()
        {
            var s = Id.ToString();
            var shortId = s.Substring(0, Math.Min(8, s.Length));
            return Role + ": " + Name + " (" + shortId + ")";
        }
    }

    public sealed class Archaeologist : Character
    {
        private readonly IInventoryItem _tool;
        private readonly Random _rng;

        public Archaeologist(string name, IInventoryItem tool, Random rng) : base(name, Role.Archaeologist)
        {
            _tool = tool;
            _rng = rng;
        }

        public override void Act(World world)
        {
            if (world.Weather == WeatherState.Sandstorm)
            {
                world.Logger.Log(new LogEntry(DateTime.Now, this + ": пережидает песчаную бурю."));
                return;
            }

            world.Logger.Log(new LogEntry(DateTime.Now, this + ": начинает аккуратную расчистку сектора " + _rng.Next(1, 5) + " инструментом «" + _tool.Title + "»."));

            try
            {
                _tool.Use();
                if (_rng.NextDouble() < 0.6)
                {
                    var kinds = (ArtifactType[])Enum.GetValues(typeof(ArtifactType));
                    var kind = kinds[_rng.Next(0, kinds.Length)];
                    var found = new Artifact(kind, Math.Round(_rng.NextDouble() * 1.5 + 0.2, 2));
                    world.AddArtifact(found);
                    world.Logger.Log(new LogEntry(DateTime.Now, this + ": обнаружен артефакт — " + found + "."));
                }
                else
                {
                    world.Logger.Log(new LogEntry(DateTime.Now, this + ": тщательный осмотр — без находок."));
                }
            }
            catch (ToolBrokenException ex)
            {
                world.Logger.Log(new LogEntry(DateTime.Now, this + ": " + ex.Message));
            }
        }
    }

    public sealed class Engineer : Character
    {
        private readonly SurveyDrone _drone;
        public Engineer(string name, SurveyDrone drone) : base(name, Role.Engineer)
        {
            _drone = drone;
        }

        public override void Act(World world)
        {
            if (_drone.IsBroken)
            {
                world.Logger.Log(new LogEntry(DateTime.Now, this + ": ремонтирует дрон..."));
                _drone.Repair();
                world.Logger.Log(new LogEntry(DateTime.Now, this + ": дрон готов к работе."));
            }
            else
            {
                if (world.Weather == WeatherState.Sandstorm)
                {
                    world.Logger.Log(new LogEntry(DateTime.Now, this + ": откладывает запуск дрона из-за бури."));
                    return;
                }
                _drone.Survey(world);
            }
        }
    }

    public sealed class Guide : Character
    {
        private int _water;
        public Guide(string name, int water) : base(name, Role.Guide) { _water = water; }

        public override void Act(World world)
        {
            try
            {
                const int cost = 2;
                if (_water < cost)
                    throw new NotEnoughSuppliesException("Полевой выход на дальний участок", cost, _water);

                _water -= cost;
                world.Logger.Log(new LogEntry(DateTime.Now, this + ": планирует безопасный маршрут; расход воды " + cost + ", остаток " + _water + "."));

                var index = world.Rng.Next(0, 6);
                var forecast = world.Forecast[index]; // может бросить IndexOutOfRangeException
                world.Logger.Log(new LogEntry(DateTime.Now, this + ": сверка с прогнозом: «" + forecast + "»."));
            }
            catch (NotEnoughSuppliesException ex)
            {
                world.Logger.Log(new LogEntry(DateTime.Now, this + ": " + ex.Message + " Принято решение остаться в лагере и пополнить запасы."));
            }
            catch (IndexOutOfRangeException ex)
            {
                world.Logger.Log(new LogEntry(DateTime.Now, this + ": непредвиденная ошибка прогноза (несоответствие данных): " + ex.GetType().Name + ". Используем запасной план."));
            }
        }
    }
    #endregion

    #region Items & Devices
    public sealed class BrushTool : IInventoryItem, IEquatable<BrushTool>
    {
        public string Title { get; private set; }
        public double Weight { get; private set; }
        public int Durability { get; private set; }

        public BrushTool(string title, double weight, int durability)
        {
            Title = title;
            Weight = weight;
            Durability = durability;
        }

        public void Use()
        {
            if (Durability <= 0)
                throw new ToolBrokenException(Title);
            Durability--;
            if (Durability == 0)
                throw new ToolBrokenException(Title);
        }

        public override string ToString() { return Title + " (вес " + Weight + " кг, прочность " + Durability + ")"; }

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + (Title == null ? 0 : Title.GetHashCode());
                h = h * 31 + Weight.GetHashCode();
                return h;
            }
        }

        public override bool Equals(object obj) { return Equals(obj as BrushTool); }

        public bool Equals(BrushTool other)
        {
            return other != null &&
                   other.Title == Title &&
                   Math.Abs(other.Weight - Weight) < 1e-9;
        }
    }

    public sealed class Artifact : IInventoryItem, IEquatable<Artifact>
    {
        private static readonly Random s_rng = new Random();

        public ArtifactType Kind { get; private set; }
        public string Title { get { return Kind.ToString(); } }
        public double Weight { get; private set; }
        public Coordinates FoundAt { get; private set; }

        public Artifact(ArtifactType kind, double weight)
        {
            Kind = kind;
            Weight = weight;
            FoundAt = new Coordinates(s_rng.Next(0, 50), s_rng.Next(0, 50));
        }

        public void Use()
        {
            throw new DomainCheckedException("Артефакт «" + Title + "» — музейная ценность, эксплуатация запрещена.");
        }

        public override string ToString()
        {
            return Kind + " (" + Weight + " кг) @ (" + FoundAt.X + "," + FoundAt.Y + ")";
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + Kind.GetHashCode();
                h = h * 31 + Weight.GetHashCode();
                h = h * 31 + FoundAt.GetHashCode();
                return h;
            }
        }

        public override bool Equals(object obj) { return Equals(obj as Artifact); }

        public bool Equals(Artifact other)
        {
            return other != null &&
                   other.Kind == Kind &&
                   Math.Abs(other.Weight - Weight) < 1e-9 &&
                   other.FoundAt.Equals(FoundAt);
        }
    }

    public sealed class SurveyDrone : Entity, IRepairable
    {
        public DroneStatus Status { get; private set; }
        public bool IsBroken { get { return Status == DroneStatus.Broken; } }

        private readonly Random _rng;

        public SurveyDrone(string name, Random rng) : base(name)
        {
            _rng = rng;
            Status = DroneStatus.Idle;
        }

        public void Survey(World world)
        {
            if (IsBroken)
            {
                world.Logger.Log(new LogEntry(DateTime.Now, this + ": неисправен, запуск невозможен."));
                return;
            }

            Status = DroneStatus.Surveying;
            world.Logger.Log(new LogEntry(DateTime.Now, this + ": взлёт и разведка ближайших квадратов..."));

            double pBreak;
            switch (world.Weather)
            {
                case WeatherState.Clear: pBreak = 0.05; break;
                case WeatherState.Windy: pBreak = 0.15; break;
                case WeatherState.Sandstorm: pBreak = 0.35; break;
                default: pBreak = 0.10; break;
            }

            if (_rng.NextDouble() < pBreak)
            {
                Status = DroneStatus.Broken;
                world.Logger.Log(new LogEntry(DateTime.Now, this + ": попал в струю песка и вышел из строя."));
            }
            else
            {
                if (_rng.NextDouble() < 0.5)
                {
                    var sector = _rng.Next(1, 6);
                    world.Logger.Log(new LogEntry(DateTime.Now, this + ": нашёл перспективный сектор #" + sector + "."));
                    world.ProspectBonus = world.ProspectBonus + 1;
                }
                Status = DroneStatus.Idle;
            }
        }

        public void Repair()
        {
            Status = DroneStatus.Idle;
        }

        public override void Act(World world)
        {
            // устройства сами по себе не действуют
        }
    }
    #endregion

    #region World + Scenario
    public sealed class World
    {
        public WeatherState Weather { get; private set; }
        public readonly Random Rng;
        public readonly ILogger Logger;

        public string[] Forecast { get; private set; }
        private readonly ArrayList _foundArtifacts = new ArrayList();

        public int ProspectBonus { get; set; }

        public World(ILogger logger, Random rng, WeatherState initialWeather, string[] forecast)
        {
            Logger = logger;
            Rng = rng;
            Weather = initialWeather;
            Forecast = forecast;
            ProspectBonus = 0;
        }

        public void AddArtifact(Artifact a) { _foundArtifacts.Add(a); }

        public IReadOnlyList<Artifact> GetArtifacts()
        {
            var list = new List<Artifact>();
            foreach (var x in _foundArtifacts) list.Add((Artifact)x);
            return list.AsReadOnly();
        }

        public void ChangeWeather(WeatherState state)
        {
            Weather = state;
            Logger.Log(new LogEntry(DateTime.Now, "Погода изменилась: " + Weather + "."));
        }
    }

    public sealed class ScenarioEngine
    {
        private readonly World _world;
        private readonly List<Entity> _actors;

        private class EventNode
        {
            public string Title { get; private set; }
            public Action Action { get; private set; }

            public EventNode(string title, Action action)
            {
                Title = title;
                Action = action;
            }

            public override string ToString() { return "Event: " + Title; }
        }

        public ScenarioEngine(World world, IEnumerable<Entity> actors)
        {
            _world = world;
            _actors = new List<Entity>(actors);
        }

        public void RunDay(int steps)
        {
            _world.Logger.Log(new LogEntry(DateTime.Now, "== Начало дня экспедиции =="));

            var nodes = BuildEventChain(steps);

            foreach (var node in nodes)
            {
                _world.Logger.Log(new LogEntry(DateTime.Now, "-- " + node.Title));
                try
                {
                    node.Action();
                }
                catch (DomainCheckedException ex)
                {
                    _world.Logger.Log(new LogEntry(DateTime.Now, "Обработано доменное исключение: " + ex.Message));
                }
                catch (Exception ex)
                {
                    _world.Logger.Log(new LogEntry(DateTime.Now, "Неожиданная ошибка: " + ex.GetType().Name + ": " + ex.Message));
                }
            }

            _world.Logger.Log(new LogEntry(DateTime.Now, "== Завершение дня экспедиции =="));
        }

        private IEnumerable<EventNode> BuildEventChain(int steps)
        {
            var list = new List<EventNode>();

            EventNode Make(string title, Action action) { return new EventNode(title, action); }

            list.Add(Make("Утренний брифинг и оценка погоды", () =>
            {
                var next = _world.Rng.NextDouble();
                var state = next < 0.6 ? WeatherState.Clear : (next < 0.85 ? WeatherState.Windy : WeatherState.Sandstorm);
                _world.ChangeWeather(state);
            }));

            for (int i = 0; i < steps; i++)
            {
                int idx = i;
                list.Add(Make("Действия отряда — фаза " + (idx + 1), () =>
                {
                    // Перемешиваем порядок действий
                    var shuffled = new List<Entity>(_actors);
                    for (int k = shuffled.Count - 1; k > 0; k--)
                    {
                        int j = _world.Rng.Next(0, k + 1);
                        var tmp = shuffled[k]; shuffled[k] = shuffled[j]; shuffled[j] = tmp;
                    }

                    foreach (var actor in shuffled)
                        actor.Act(_world);

                    if (_world.Rng.NextDouble() < 0.15 && _world.Weather != WeatherState.Sandstorm)
                        _world.ChangeWeather(WeatherState.Sandstorm);
                }));
            }

            list.Add(Make("Вечерняя инвентаризация", () =>
            {
                var arts = _world.GetArtifacts();

                int count = arts.Count;
                double totalWeight = 0.0;
                var dict = new Dictionary<ArtifactType, int>();
                foreach (var a in arts)
                {
                    totalWeight += a.Weight;
                    if (!dict.ContainsKey(a.Kind)) dict[a.Kind] = 0;
                    dict[a.Kind] = dict[a.Kind] + 1;
                }
                totalWeight = Math.Round(totalWeight, 3);

                string kinds = string.Join(", ", dict.Select(g => g.Key + ":" + g.Value).ToArray());
                _world.Logger.Log(new LogEntry(DateTime.Now, "Сводка находок: всего " + count + ", общий вес " + totalWeight + " кг; типы: " + kinds + "."));
            }));

            return list;
        }
    }
    #endregion

    public static class Program
    {
        public static void Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            void Header(string title)
            {
                Console.WriteLine(new string('=', title.Length + 8));
                Console.WriteLine("=== " + title + " ===");
                Console.WriteLine(new string('=', title.Length + 8));
            }

            Header("Археологическая экспедиция — Лаба 1 (ООП + SOLID)");

            var rng = new Random();
            
            var forecast = new[] { "ясно", "ветер", "песчаная буря" };
            var world = new World(new ConsoleLogger(), rng, WeatherState.Clear, forecast);

            var brush = new BrushTool("Кисть №5", 0.2, rng.Next(1, 4));
            var drone = new SurveyDrone("«Глаз-Сокол»", rng);
            //исправил незначительные ошибки
            var team = new List<Entity>
            {
                new Archaeologist("Инесса", brush, rng),
                new Engineer("Савелий", drone),
                new Guide("Лейла", rng.Next(0, 5))
            };

            var engine = new ScenarioEngine(world, team);
            engine.RunDay(rng.Next(3, 5));

            Console.WriteLine();
            Console.WriteLine("Нажмите любую клавишу, чтобы завершить...");
            Console.ReadKey();
        }
    }
}