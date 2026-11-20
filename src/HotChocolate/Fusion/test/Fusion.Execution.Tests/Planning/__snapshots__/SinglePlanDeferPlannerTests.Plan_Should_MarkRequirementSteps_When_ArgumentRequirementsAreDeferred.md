# Plan_Should_MarkRequirementSteps_When_ArgumentRequirementsAreDeferred

## d07

```yaml
operation:
  - document: |
      {
        products {
          eur
          usd
          upc @fusion__requirement
          price(cur: "USD") @fusion__requirement
          fusion__requirement_price_0: price(cur: "EUR") @fusion__requirement
          ... @defer(__group: 0, __parent: -1) {
            both
            upc @fusion__requirement
            fusion__requirement_price_1: price(cur: "EUR", tax: true)
              @fusion__requirement
          }
        }
      }
    hash: 123456789101112
    searchSpace: 3
    expandedNodes: 19
nodes:
  - id: 1
    type: Operation
    schema: a
    operation: |
      query Op_123456789101112_1 {
        products {
          upc
        }
      }
  - id: 2
    type: Operation
    schema: a
    operation: |
      query Op_123456789101112_2(
        $__fusion_1_price: Int
        $__fusion_2_upc: String!
        $__fusion_5_price: Int
      ) {
        productByUpc(upc: $__fusion_2_upc) {
          usd(price: $__fusion_1_price)
          eur(price: $__fusion_5_price)
        }
      }
    source: $.productByUpc
    target: $.products
    requirements:
      - name: __fusion_1_price
        selectionMap: >-
          price(cur: "USD")
      - name: __fusion_2_upc
        selectionMap: >-
          upc
      - name: __fusion_5_price
        internalAlias: fusion__requirement_price_0
        selectionMap: >-
          price(cur: "EUR")
    dependencies:
      - id: 1
      - id: 3
  - id: 3
    type: Operation
    schema: b
    operation: |
      query Op_123456789101112_3($__fusion_3_upc: String!) {
        productByUpcB(upc: $__fusion_3_upc) {
          price(cur: "USD")
          fusion__requirement_price_0: price(cur: "EUR")
        }
      }
    source: $.productByUpcB
    target: $.products
    requirements:
      - name: __fusion_3_upc
        selectionMap: >-
          upc
    dependencies:
      - id: 1
  - id: 6
    type: Operation
    schema: a
    operation: |
      query Op_123456789101112_6($__fusion_7_price: Int, $__fusion_8_upc: String!) {
        productByUpc(upc: $__fusion_8_upc) {
          both(price: $__fusion_7_price)
        }
      }
    source: $.productByUpc
    target: $.products
    requirements:
      - name: __fusion_7_price
        internalAlias: fusion__requirement_price_1
        selectionMap: >-
          price(cur: "EUR", tax: true)
      - name: __fusion_8_upc
        selectionMap: >-
          upc
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
      - id: 7
  - id: 7
    type: Operation
    schema: b
    operation: |
      query Op_123456789101112_7($__fusion_9_upc: String!) {
        productByUpcB(upc: $__fusion_9_upc) {
          fusion__requirement_price_1: price(cur: "EUR", tax: true)
        }
      }
    source: $.productByUpcB
    target: $.products
    requirements:
      - name: __fusion_9_upc
        selectionMap: >-
          upc
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
deliveryGroups:
  - id: 0
    path: $.products

```

## d09

```yaml
operation:
  - document: |
      {
        products {
          usd
          upc @fusion__requirement
          price(cur: "USD") @fusion__requirement
          ... @defer(__group: 0, __parent: -1) {
            eur
            upc @fusion__requirement
            fusion__requirement_price_0: price(cur: "EUR") @fusion__requirement
          }
        }
      }
    hash: 123456789101112
    searchSpace: 3
    expandedNodes: 17
nodes:
  - id: 1
    type: Operation
    schema: a
    operation: |
      query Op_123456789101112_1 {
        products {
          upc
        }
      }
  - id: 2
    type: Operation
    schema: a
    operation: |
      query Op_123456789101112_2($__fusion_1_price: Int, $__fusion_2_upc: String!) {
        productByUpc(upc: $__fusion_2_upc) {
          usd(price: $__fusion_1_price)
        }
      }
    source: $.productByUpc
    target: $.products
    requirements:
      - name: __fusion_1_price
        selectionMap: >-
          price(cur: "USD")
      - name: __fusion_2_upc
        selectionMap: >-
          upc
    dependencies:
      - id: 1
      - id: 3
  - id: 3
    type: Operation
    schema: b
    operation: |
      query Op_123456789101112_3($__fusion_3_upc: String!) {
        productByUpcB(upc: $__fusion_3_upc) {
          price(cur: "USD")
        }
      }
    source: $.productByUpcB
    target: $.products
    requirements:
      - name: __fusion_3_upc
        selectionMap: >-
          upc
    dependencies:
      - id: 1
  - id: 6
    type: Operation
    schema: a
    operation: |
      query Op_123456789101112_6($__fusion_6_price: Int, $__fusion_7_upc: String!) {
        productByUpc(upc: $__fusion_7_upc) {
          eur(price: $__fusion_6_price)
        }
      }
    source: $.productByUpc
    target: $.products
    requirements:
      - name: __fusion_6_price
        internalAlias: fusion__requirement_price_0
        selectionMap: >-
          price(cur: "EUR")
      - name: __fusion_7_upc
        selectionMap: >-
          upc
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
      - id: 7
  - id: 7
    type: Operation
    schema: b
    operation: |
      query Op_123456789101112_7($__fusion_8_upc: String!) {
        productByUpcB(upc: $__fusion_8_upc) {
          fusion__requirement_price_0: price(cur: "EUR")
        }
      }
    source: $.productByUpcB
    target: $.products
    requirements:
      - name: __fusion_8_upc
        selectionMap: >-
          upc
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
deliveryGroups:
  - id: 0
    path: $.products

```

## d13

```yaml
operation:
  - document: |
      {
        products {
          upc
          ... @defer(__group: 0, __parent: -1) {
            usd
            upc @fusion__requirement
            price(cur: "USD") @fusion__requirement
          }
        }
      }
    hash: 123456789101112
    searchSpace: 2
    expandedNodes: 8
nodes:
  - id: 1
    type: Operation
    schema: a
    operation: |
      query Op_123456789101112_1 {
        products {
          upc
        }
      }
  - id: 3
    type: Operation
    schema: a
    operation: |
      query Op_123456789101112_3($__fusion_2_price: Int, $__fusion_3_upc: String!) {
        productByUpc(upc: $__fusion_3_upc) {
          usd(price: $__fusion_2_price)
        }
      }
    source: $.productByUpc
    target: $.products
    requirements:
      - name: __fusion_2_price
        selectionMap: >-
          price(cur: "USD")
      - name: __fusion_3_upc
        selectionMap: >-
          upc
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
      - id: 4
  - id: 4
    type: Operation
    schema: b
    operation: |
      query Op_123456789101112_4($__fusion_4_upc: String!) {
        productByUpcB(upc: $__fusion_4_upc) {
          price(cur: "USD")
        }
      }
    source: $.productByUpcB
    target: $.products
    requirements:
      - name: __fusion_4_upc
        selectionMap: >-
          upc
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
deliveryGroups:
  - id: 0
    path: $.products

```

## d14

```yaml
operation:
  - document: |
      {
        products {
          upc
          ... @defer(__group: 0, __parent: -1) {
            eur
            upc @fusion__requirement
            fusion__requirement_price_0: price(cur: "EUR") @fusion__requirement
          }
          ... @defer(__group: 1, __parent: -1) {
            usd
            upc @fusion__requirement
            price(cur: "USD") @fusion__requirement
          }
        }
      }
    hash: 123456789101112
    searchSpace: 3
    expandedNodes: 18
nodes:
  - id: 1
    type: Operation
    schema: a
    operation: |
      query Op_123456789101112_1 {
        products {
          upc
        }
      }
  - id: 4
    type: Operation
    schema: b
    operation: |
      query Op_123456789101112_4($__fusion_4_upc: String!) {
        productByUpcB(upc: $__fusion_4_upc) {
          price(cur: "USD")
        }
      }
    source: $.productByUpcB
    target: $.products
    batchingGroupId: 10
    requirements:
      - name: __fusion_4_upc
        selectionMap: >-
          upc
    deliveryGroups:
      - 1
    dependencies:
      - id: 1
  - id: 8
    type: Operation
    schema: b
    operation: |
      query Op_123456789101112_8($__fusion_9_upc: String!) {
        productByUpcB(upc: $__fusion_9_upc) {
          fusion__requirement_price_0: price(cur: "EUR")
        }
      }
    source: $.productByUpcB
    target: $.products
    batchingGroupId: 10
    requirements:
      - name: __fusion_9_upc
        selectionMap: >-
          upc
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
  - id: 3
    type: Operation
    schema: a
    operation: |
      query Op_123456789101112_3($__fusion_2_price: Int, $__fusion_3_upc: String!) {
        productByUpc(upc: $__fusion_3_upc) {
          usd(price: $__fusion_2_price)
        }
      }
    source: $.productByUpc
    target: $.products
    batchingGroupId: 11
    requirements:
      - name: __fusion_2_price
        selectionMap: >-
          price(cur: "USD")
      - name: __fusion_3_upc
        selectionMap: >-
          upc
    deliveryGroups:
      - 1
    dependencies:
      - id: 1
      - id: 4
  - id: 7
    type: Operation
    schema: a
    operation: |
      query Op_123456789101112_7($__fusion_7_price: Int, $__fusion_8_upc: String!) {
        productByUpc(upc: $__fusion_8_upc) {
          eur(price: $__fusion_7_price)
        }
      }
    source: $.productByUpc
    target: $.products
    batchingGroupId: 11
    requirements:
      - name: __fusion_7_price
        internalAlias: fusion__requirement_price_0
        selectionMap: >-
          price(cur: "EUR")
      - name: __fusion_8_upc
        selectionMap: >-
          upc
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
      - id: 8
deliveryGroups:
  - id: 0
    path: $.products
  - id: 1
    path: $.products

```

## d15

```yaml
operation:
  - document: |
      {
        products {
          upc
          ... @defer(__group: 0, __parent: -1) {
            eur
            price
            upc @fusion__requirement
            fusion__requirement_price_0: price(cur: "EUR") @fusion__requirement
          }
        }
      }
    hash: 123456789101112
    searchSpace: 2
    expandedNodes: 6
nodes:
  - id: 1
    type: Operation
    schema: a
    operation: |
      query Op_123456789101112_1 {
        products {
          upc
        }
      }
  - id: 2
    type: Operation
    schema: b
    operation: |
      query Op_123456789101112_2($__fusion_1_upc: String!) {
        productByUpcB(upc: $__fusion_1_upc) {
          price
          upc
          fusion__requirement_price_0: price(cur: "EUR")
        }
      }
    source: $.productByUpcB
    target: $.products
    requirements:
      - name: __fusion_1_upc
        selectionMap: >-
          upc
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
  - id: 3
    type: Operation
    schema: a
    operation: |
      query Op_123456789101112_3($__fusion_2_upc: String!, $__fusion_3_price: Int) {
        productByUpc(upc: $__fusion_2_upc) {
          eur(price: $__fusion_3_price)
        }
      }
    source: $.productByUpc
    target: $.products
    requirements:
      - name: __fusion_2_upc
        selectionMap: >-
          upc
      - name: __fusion_3_price
        internalAlias: fusion__requirement_price_0
        selectionMap: >-
          price(cur: "EUR")
    deliveryGroups:
      - 0
    dependencies:
      - id: 2
deliveryGroups:
  - id: 0
    path: $.products

```

## d18

```yaml
operation:
  - document: |
      {
        products {
          upc
          ... @defer(__group: 0, __parent: -1) {
            eur
            price
            usd
            upc @fusion__requirement
            fusion__requirement_price_0: price(cur: "USD") @fusion__requirement
            fusion__requirement_price_1: price(cur: "EUR") @fusion__requirement
          }
        }
      }
    hash: 123456789101112
    searchSpace: 2
    expandedNodes: 6
nodes:
  - id: 1
    type: Operation
    schema: a
    operation: |
      query Op_123456789101112_1 {
        products {
          upc
        }
      }
  - id: 2
    type: Operation
    schema: b
    operation: |
      query Op_123456789101112_2($__fusion_1_upc: String!) {
        productByUpcB(upc: $__fusion_1_upc) {
          price
          upc
          fusion__requirement_price_0: price(cur: "USD")
          fusion__requirement_price_1: price(cur: "EUR")
        }
      }
    source: $.productByUpcB
    target: $.products
    requirements:
      - name: __fusion_1_upc
        selectionMap: >-
          upc
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
  - id: 3
    type: Operation
    schema: a
    operation: |
      query Op_123456789101112_3(
        $__fusion_2_upc: String!
        $__fusion_3_price: Int
        $__fusion_4_price: Int
      ) {
        productByUpc(upc: $__fusion_2_upc) {
          usd(price: $__fusion_3_price)
          eur(price: $__fusion_4_price)
        }
      }
    source: $.productByUpc
    target: $.products
    requirements:
      - name: __fusion_2_upc
        selectionMap: >-
          upc
      - name: __fusion_3_price
        internalAlias: fusion__requirement_price_0
        selectionMap: >-
          price(cur: "USD")
      - name: __fusion_4_price
        internalAlias: fusion__requirement_price_1
        selectionMap: >-
          price(cur: "EUR")
    deliveryGroups:
      - 0
    dependencies:
      - id: 2
deliveryGroups:
  - id: 0
    path: $.products

```

## v16c

```yaml
operation:
  - document: |
      {
        products {
          price
          upc @fusion__requirement
          ... @defer(__group: 0, __parent: -1) {
            eur
            upc @fusion__requirement
            fusion__requirement_price_0: price(cur: "EUR") @fusion__requirement
          }
        }
      }
    hash: 123456789101112
    searchSpace: 2
    expandedNodes: 9
nodes:
  - id: 1
    type: Operation
    schema: a
    operation: |
      query Op_123456789101112_1 {
        products {
          upc
        }
      }
  - id: 2
    type: Operation
    schema: b
    operation: |
      query Op_123456789101112_2($__fusion_1_upc: String!) {
        productByUpcB(upc: $__fusion_1_upc) {
          price
        }
      }
    source: $.productByUpcB
    target: $.products
    requirements:
      - name: __fusion_1_upc
        selectionMap: >-
          upc
    dependencies:
      - id: 1
  - id: 4
    type: Operation
    schema: a
    operation: |
      query Op_123456789101112_4($__fusion_3_price: Int, $__fusion_4_upc: String!) {
        productByUpc(upc: $__fusion_4_upc) {
          eur(price: $__fusion_3_price)
        }
      }
    source: $.productByUpc
    target: $.products
    requirements:
      - name: __fusion_3_price
        internalAlias: fusion__requirement_price_0
        selectionMap: >-
          price(cur: "EUR")
      - name: __fusion_4_upc
        selectionMap: >-
          upc
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
      - id: 5
  - id: 5
    type: Operation
    schema: b
    operation: |
      query Op_123456789101112_5($__fusion_5_upc: String!) {
        productByUpcB(upc: $__fusion_5_upc) {
          fusion__requirement_price_0: price(cur: "EUR")
        }
      }
    source: $.productByUpcB
    target: $.products
    requirements:
      - name: __fusion_5_upc
        selectionMap: >-
          upc
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
deliveryGroups:
  - id: 0
    path: $.products

```

## w01

```yaml
operation:
  - document: |
      {
        product {
          upc
          ... @defer(__group: 0, __parent: -1) {
            eur
            upc @fusion__requirement
            price(cur: "EUR") @fusion__requirement
          }
        }
      }
    hash: 123456789101112
    searchSpace: 2
    expandedNodes: 8
nodes:
  - id: 1
    type: Operation
    schema: a
    operation: |
      query Op_123456789101112_1 {
        product {
          upc
        }
      }
  - id: 3
    type: Operation
    schema: a
    operation: |
      query Op_123456789101112_3($__fusion_2_price: Int, $__fusion_3_upc: String!) {
        productByUpc(upc: $__fusion_3_upc) {
          eur(price: $__fusion_2_price)
        }
      }
    source: $.productByUpc
    target: $.product
    requirements:
      - name: __fusion_2_price
        selectionMap: >-
          price(cur: "EUR")
      - name: __fusion_3_upc
        selectionMap: >-
          upc
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
      - id: 4
  - id: 4
    type: Operation
    schema: b
    operation: |
      query Op_123456789101112_4($__fusion_4_upc: String!) {
        productByUpcB(upc: $__fusion_4_upc) {
          price(cur: "EUR")
        }
      }
    source: $.productByUpcB
    target: $.product
    requirements:
      - name: __fusion_4_upc
        selectionMap: >-
          upc
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
deliveryGroups:
  - id: 0
    path: $.product

```

## y01

```yaml
operation:
  - document: |
      {
        product {
          upc
          ... @defer(__group: 0, __parent: -1) {
            eurC
            upc @fusion__requirement
            price(cur: "EUR") @fusion__requirement
          }
        }
      }
    hash: 123456789101112
    searchSpace: 2
    expandedNodes: 6
nodes:
  - id: 1
    type: Operation
    schema: a
    operation: |
      query Op_123456789101112_1 {
        product {
          upc
        }
      }
  - id: 3
    type: Operation
    schema: c
    operation: |
      query Op_123456789101112_3($__fusion_2_price: Int, $__fusion_3_upc: String!) {
        productByUpcC(upc: $__fusion_3_upc) {
          eurC(price: $__fusion_2_price)
        }
      }
    source: $.productByUpcC
    target: $.product
    requirements:
      - name: __fusion_2_price
        selectionMap: >-
          price(cur: "EUR")
      - name: __fusion_3_upc
        selectionMap: >-
          upc
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
      - id: 4
  - id: 4
    type: Operation
    schema: b
    operation: |
      query Op_123456789101112_4($__fusion_4_upc: String!) {
        productByUpcB(upc: $__fusion_4_upc) {
          price(cur: "EUR")
        }
      }
    source: $.productByUpcB
    target: $.product
    requirements:
      - name: __fusion_4_upc
        selectionMap: >-
          upc
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
deliveryGroups:
  - id: 0
    path: $.product

```

## y05

```yaml
operation:
  - document: |
      {
        products {
          upc
          ... @defer(__group: 0, __parent: -1) {
            eurC
            eur
            upc @fusion__requirement
            price(cur: "EUR") @fusion__requirement
          }
        }
      }
    hash: 123456789101112
    searchSpace: 3
    expandedNodes: 13
nodes:
  - id: 1
    type: Operation
    schema: a
    operation: |
      query Op_123456789101112_1 {
        products {
          upc
        }
      }
  - id: 3
    type: Operation
    schema: c
    operation: |
      query Op_123456789101112_3($__fusion_2_price: Int, $__fusion_3_upc: String!) {
        productByUpcC(upc: $__fusion_3_upc) {
          eurC(price: $__fusion_2_price)
        }
      }
    source: $.productByUpcC
    target: $.products
    requirements:
      - name: __fusion_2_price
        selectionMap: >-
          price(cur: "EUR")
      - name: __fusion_3_upc
        selectionMap: >-
          upc
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
      - id: 4
  - id: 4
    type: Operation
    schema: b
    operation: |
      query Op_123456789101112_4($__fusion_4_upc: String!) {
        productByUpcB(upc: $__fusion_4_upc) {
          price(cur: "EUR")
        }
      }
    source: $.productByUpcB
    target: $.products
    requirements:
      - name: __fusion_4_upc
        selectionMap: >-
          upc
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
  - id: 5
    type: Operation
    schema: a
    operation: |
      query Op_123456789101112_5($__fusion_5_upc: String!, $__fusion_6_price: Int) {
        productByUpc(upc: $__fusion_5_upc) {
          eur(price: $__fusion_6_price)
        }
      }
    source: $.productByUpc
    target: $.products
    requirements:
      - name: __fusion_5_upc
        selectionMap: >-
          upc
      - name: __fusion_6_price
        selectionMap: >-
          price(cur: "EUR")
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
      - id: 4
deliveryGroups:
  - id: 0
    path: $.products

```

## n1

```yaml
operation:
  - document: |
      {
        products {
          upc
          ... @defer(__group: 0, __parent: -1) {
            netWeight
            upc @fusion__requirement
            weight @fusion__requirement
          }
        }
      }
    hash: 123456789101112
    searchSpace: 2
    expandedNodes: 8
nodes:
  - id: 1
    type: Operation
    schema: a
    operation: |
      query Op_123456789101112_1 {
        products {
          upc
        }
      }
  - id: 3
    type: Operation
    schema: a
    operation: |
      query Op_123456789101112_3($__fusion_2_weight: Int, $__fusion_3_upc: String!) {
        productByUpc(upc: $__fusion_3_upc) {
          netWeight(weight: $__fusion_2_weight)
        }
      }
    source: $.productByUpc
    target: $.products
    requirements:
      - name: __fusion_2_weight
        selectionMap: >-
          weight
      - name: __fusion_3_upc
        selectionMap: >-
          upc
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
      - id: 4
  - id: 4
    type: Operation
    schema: b
    operation: |
      query Op_123456789101112_4($__fusion_4_upc: String!) {
        productByUpcB(upc: $__fusion_4_upc) {
          weight
        }
      }
    source: $.productByUpcB
    target: $.products
    requirements:
      - name: __fusion_4_upc
        selectionMap: >-
          upc
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
deliveryGroups:
  - id: 0
    path: $.products

```

## n2

```yaml
operation:
  - document: |
      {
        products {
          upc
          ... @defer(__group: 0, __parent: -1) {
            weightC
            upc @fusion__requirement
            weight @fusion__requirement
          }
        }
      }
    hash: 123456789101112
    searchSpace: 2
    expandedNodes: 6
nodes:
  - id: 1
    type: Operation
    schema: a
    operation: |
      query Op_123456789101112_1 {
        products {
          upc
        }
      }
  - id: 3
    type: Operation
    schema: c
    operation: |
      query Op_123456789101112_3($__fusion_2_weight: Int, $__fusion_3_upc: String!) {
        productByUpcC(upc: $__fusion_3_upc) {
          weightC(weight: $__fusion_2_weight)
        }
      }
    source: $.productByUpcC
    target: $.products
    requirements:
      - name: __fusion_2_weight
        selectionMap: >-
          weight
      - name: __fusion_3_upc
        selectionMap: >-
          upc
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
      - id: 4
  - id: 4
    type: Operation
    schema: b
    operation: |
      query Op_123456789101112_4($__fusion_4_upc: String!) {
        productByUpcB(upc: $__fusion_4_upc) {
          weight
        }
      }
    source: $.productByUpcB
    target: $.products
    requirements:
      - name: __fusion_4_upc
        selectionMap: >-
          upc
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
deliveryGroups:
  - id: 0
    path: $.products

```

## n3

```yaml
operation:
  - document: |
      {
        products {
          upc
          ... @defer(__group: 0, __parent: -1) {
            chained
            upc @fusion__requirement
            eur @fusion__requirement
            price(cur: "EUR") @fusion__requirement
          }
        }
      }
    hash: 123456789101112
    searchSpace: 2
    expandedNodes: 9
nodes:
  - id: 1
    type: Operation
    schema: a
    operation: |
      query Op_123456789101112_1 {
        products {
          upc
        }
      }
  - id: 3
    type: Operation
    schema: c
    operation: |
      query Op_123456789101112_3($__fusion_2_eur: Int, $__fusion_3_upc: String!) {
        productByUpcC(upc: $__fusion_3_upc) {
          chained(eur: $__fusion_2_eur)
        }
      }
    source: $.productByUpcC
    target: $.products
    requirements:
      - name: __fusion_2_eur
        selectionMap: >-
          eur
      - name: __fusion_3_upc
        selectionMap: >-
          upc
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
      - id: 5
  - id: 5
    type: Operation
    schema: a
    operation: |
      query Op_123456789101112_5($__fusion_5_price: Int, $__fusion_6_upc: String!) {
        productByUpc(upc: $__fusion_6_upc) {
          eur(price: $__fusion_5_price)
        }
      }
    source: $.productByUpc
    target: $.products
    requirements:
      - name: __fusion_5_price
        selectionMap: >-
          price(cur: "EUR")
      - name: __fusion_6_upc
        selectionMap: >-
          upc
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
      - id: 6
  - id: 6
    type: Operation
    schema: b
    operation: |
      query Op_123456789101112_6($__fusion_7_upc: String!) {
        productByUpcB(upc: $__fusion_7_upc) {
          price(cur: "EUR")
        }
      }
    source: $.productByUpcB
    target: $.products
    requirements:
      - name: __fusion_7_upc
        selectionMap: >-
          upc
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
deliveryGroups:
  - id: 0
    path: $.products

```

## n4

```yaml
operation:
  - document: |
      query($v: Boolean!) {
        products {
          upc
          upc @fusion__requirement
          price(cur: "EUR") @fusion__requirement
          eurC @skip(if: $v)
          ... @include(if: $v) {
            ... @defer(if: $v, __group: 0, __parent: -1) {
              eurC
              upc @fusion__requirement
              price(cur: "EUR") @fusion__requirement
            }
          }
        }
      }
    hash: 123456789101112
    searchSpace: 2
    expandedNodes: 10
nodes:
  - id: 1
    type: Operation
    schema: a
    operation: |
      query Op_123456789101112_1 {
        products {
          upc
        }
      }
  - id: 3
    type: Operation
    schema: c
    operation: |
      query Op_123456789101112_3($__fusion_2_price: Int, $__fusion_3_upc: String!) {
        productByUpcC(upc: $__fusion_3_upc) {
          eurC(price: $__fusion_2_price)
        }
      }
    source: $.productByUpcC
    target: $.products
    requirements:
      - name: __fusion_2_price
        selectionMap: >-
          price(cur: "EUR")
      - name: __fusion_3_upc
        selectionMap: >-
          upc
    conditions:
      - variable: $v
        passingValue: false
    dependencies:
      - id: 1
      - id: 4
  - id: 4
    type: Operation
    schema: b
    operation: |
      query Op_123456789101112_4($__fusion_4_upc: String!) {
        productByUpcB(upc: $__fusion_4_upc) {
          price(cur: "EUR")
        }
      }
    source: $.productByUpcB
    target: $.products
    requirements:
      - name: __fusion_4_upc
        selectionMap: >-
          upc
    conditions:
      - variable: $v
        passingValue: false
    dependencies:
      - id: 1
  - id: 6
    type: Operation
    schema: c
    operation: |
      query Op_123456789101112_6($__fusion_6_price: Int, $__fusion_7_upc: String!) {
        productByUpcC(upc: $__fusion_7_upc) {
          eurC(price: $__fusion_6_price)
        }
      }
    source: $.productByUpcC
    target: $.products
    requirements:
      - name: __fusion_6_price
        selectionMap: >-
          price(cur: "EUR")
      - name: __fusion_7_upc
        selectionMap: >-
          upc
    conditions:
      - variable: $v
        passingValue: true
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
      - id: 7
  - id: 7
    type: Operation
    schema: b
    operation: |
      query Op_123456789101112_7($__fusion_8_upc: String!) {
        productByUpcB(upc: $__fusion_8_upc) {
          price(cur: "EUR")
        }
      }
    source: $.productByUpcB
    target: $.products
    requirements:
      - name: __fusion_8_upc
        selectionMap: >-
          upc
    conditions:
      - variable: $v
        passingValue: true
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
deliveryGroups:
  - id: 0
    path: $.products
    ifVariable: $v

```
