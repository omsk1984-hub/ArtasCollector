# План: Writer (C#) не пишет стакан — пустые/инвертированные market_pairs

## Проблема 1 — market_pairs нулевые (первая итерация)

Фрактал `id=1791541904877` (symbol_id=1, price=85845.0) записан с пустым стаканом:

```json
"peak": {
  "Ask": 0, "Bid": 0,
  "market_pairs": [
    { "price": 0, "is_ask": true, "volume": 0 },
    { "price": 0, "is_ask": false, "volume": 0 }
  ]
},
"info": {
  "Ask": 85846, "Bid": 85845, "Volume": 12,
  "Markets": [ { "price": 85846, "volume": 0 }, ... { "price": 85845, "volume": 0 } ]
}
```

### Корневые причины (1)

1. **`ConvertToMyTick` игнорировал стакан.** Пары `MyTick.MarketPairs`
   формировались из котировочных `evt.Ask`/`evt.Bid` с зашитым `volume: 0`,
   а реальный стакан не использовался. В Python-эталоне
   `convert_to_my_tick(tick, market_item)` берёт `market_item.market_pairs`.
2. **Finam-котировки идут из сделок.** `PublishTrade` публикует `QuoteEvent`
   с `Ask=0, Bid=0` (из `SubscribeLatestTrades`) → пары из котировки пустые.
3. **Инкрементальный стакан не накапливался** — `PublishOrderBook` игнорировал
   `action` (`ADD`/`UPDATE`/`REMOVE`).

### Исправление (1)
- `TickService.ConvertToMyTick` → берёт пары из `_marketService.CurrentMarkets`.
- `FinamGrpcClient.SymbolOrderBook` → накопление инкрементального стакана с учётом
  `action` и снапшота.

## Проблема 2 — ask < bid в health (Si)

После первой правки health показывал у Si `ask=85244 < bid=86020`.

### Диагностика
Временное логирование сырых событий Finam показало источник. Снапшот Si:

```
snapshot=True rows=[
  BuySize/Add:85176=1, BuySize/Add:85108=1, SellSize/Add:85244=1, ...,
  SellSize/Update:86052=33, BuySize/Update:86000=24, ... (реальный стакан 86000+)
]
```

Finam присылает в снапшоте «мусорные» уровни `85xxx` (объём 1), далёкие от
реального рынка Si (~86000). Они помечены `ACTION_ADD` и не удаляются, поэтому
уровень `SellSize 85244` становился «лучшим Ask» (минимальная цена).

### Исправление (2)
По требованию — убрать `maxDeep`, стакан = **2 центральных значения**:

- `MarketService` переписан: убран `maxDeep`/`BookSize`, `CurrentBook` = ровно
  `[лучший Ask, лучший Bid]`.
- Введён **фильтр по опорной цене** (`referencePrice` = последняя цена сделки
  `TickCurrent.Last`): лучший Ask = минимальная цена Ask **выше** опорной,
  лучший Bid = максимальная цена Bid **ниже** опорной. Это отсекает «мусорные»
  уровни Finam.
- `TickService.OnOrderBook` передаёт `TickCurrent.Last` как опорную цену.
- `HealthSnapshotBuilder.BuildBook` упрощён под 2 элемента.
- `InstrumentPipelineFactory`/`TickService` — конструктор без `maxDeep`.

## Результат проверки (живой Finam)

```
Si:       ask=86043 vol=13   bid=86039 vol=23    (ask > bid ✓)
USDRUBF:  ask=84.90 vol=366  bid=84.89 vol=5     (ask > bid ✓)
CNYRUBF:  ask=12.666 vol=124 bid=12.665 vol=1214 (ask > bid ✓)
```

## Тесты
- `TickServiceTests.OnOrderBook_IgnoresStaleLevels_FarFromLastPrice` — отсечение
  «мусорных» уровней по опорной цене.
- `TickServiceTests.ConvertToMyTick_UsesOrderBook_WhenAvailable`,
  `..._FallsBackToQuote_WhenNoOrderBook`.
- `FractalPipelineTests.Process_WithOrderBook_FractalCarriesNonZeroMarketPairs`.

Все 51 тест Writer проходят (`MyTick_Time_FromTimeMsc` — пред-существующее
падение, не связано с задачей).
