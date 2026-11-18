# Plan_Should_NotFallBack_When_RequirementsAreNested

## nested requirements of @require

```yaml
operation:
  - document: |
      query($a: Boolean!, $b: Boolean!) {
        product(id: "1") {
          name
          ... @skip(if: $a) {
            id @fusion__requirement
            weight @fusion__requirement
            shippingCost @skip(if: $b)
            ... @include(if: $b) {
              ... @defer(if: $b, label: "i", __group: 0, __parent: -1) {
                shippingCost
                id @fusion__requirement
                weight @fusion__requirement
              }
            }
          }
          ... @include(if: $a) {
            ... @defer(if: $a, label: "o", __group: 1, __parent: -1) {
              id @fusion__requirement
              weight @fusion__requirement
              shippingCost @skip(if: $b)
              ... @include(if: $b) {
                ... @defer(if: $b, label: "i", __group: 2, __parent: 1) {
                  shippingCost
                  id @fusion__requirement
                  weight @fusion__requirement
                }
              }
            }
          }
        }
      }
    hash: 123456789101112
    searchSpace: 2
    expandedNodes: 18
nodes:
  - id: 1
    type: Operation
    schema: a
    operation: |
      query Op_123456789101112_1($a: Boolean!, $b: Boolean!) {
        product(id: "1") {
          name
          ... @skip(if: $a) {
            ... @include(if: $b) {
              ... {
                id
              }
            }
            id
          }
          ... @include(if: $a) {
            ... {
              ... @include(if: $b) {
                ... {
                  id
                }
              }
              id
            }
          }
        }
      }
    forwardedVariables:
      - a
      - b
  - id: 3
    type: Operation
    schema: b
    operation: |
      query Op_123456789101112_3($__fusion_2_weight: Int, $__fusion_3_id: ID!) {
        productById(id: $__fusion_3_id) {
          shippingCost(weight: $__fusion_2_weight)
        }
      }
    source: $.productById
    target: $.product
    requirements:
      - name: __fusion_2_weight
        selectionMap: >-
          weight
      - name: __fusion_3_id
        selectionMap: >-
          id
    conditions:
      - variable: $a
        passingValue: true
      - variable: $b
        passingValue: false
    deliveryGroups:
      - 1
    dependencies:
      - id: 1
      - id: 4
  - id: 4
    type: Operation
    schema: c
    operation: |
      query Op_123456789101112_4($__fusion_4_id: ID!) {
        productByIdForWeight(id: $__fusion_4_id) {
          weight
        }
      }
    source: $.productByIdForWeight
    target: $.product
    requirements:
      - name: __fusion_4_id
        selectionMap: >-
          id
    conditions:
      - variable: $a
        passingValue: true
      - variable: $b
        passingValue: false
    deliveryGroups:
      - 1
    dependencies:
      - id: 1
  - id: 6
    type: Operation
    schema: b
    operation: |
      query Op_123456789101112_6($__fusion_6_weight: Int, $__fusion_7_id: ID!) {
        productById(id: $__fusion_7_id) {
          shippingCost(weight: $__fusion_6_weight)
        }
      }
    source: $.productById
    target: $.product
    requirements:
      - name: __fusion_6_weight
        selectionMap: >-
          weight
      - name: __fusion_7_id
        selectionMap: >-
          id
    conditions:
      - variable: $a
        passingValue: true
      - variable: $b
        passingValue: true
    deliveryGroups:
      - 2
    dependencies:
      - id: 1
      - id: 7
  - id: 7
    type: Operation
    schema: c
    operation: |
      query Op_123456789101112_7($__fusion_8_id: ID!) {
        productByIdForWeight(id: $__fusion_8_id) {
          weight
        }
      }
    source: $.productByIdForWeight
    target: $.product
    requirements:
      - name: __fusion_8_id
        selectionMap: >-
          id
    conditions:
      - variable: $a
        passingValue: true
      - variable: $b
        passingValue: true
    deliveryGroups:
      - 2
    dependencies:
      - id: 1
  - id: 9
    type: Operation
    schema: b
    operation: |
      query Op_123456789101112_9($__fusion_10_weight: Int, $__fusion_11_id: ID!) {
        productById(id: $__fusion_11_id) {
          shippingCost(weight: $__fusion_10_weight)
        }
      }
    source: $.productById
    target: $.product
    requirements:
      - name: __fusion_10_weight
        selectionMap: >-
          weight
      - name: __fusion_11_id
        selectionMap: >-
          id
    conditions:
      - variable: $a
        passingValue: false
      - variable: $b
        passingValue: false
    dependencies:
      - id: 1
      - id: 10
  - id: 10
    type: Operation
    schema: c
    operation: |
      query Op_123456789101112_10($__fusion_12_id: ID!) {
        productByIdForWeight(id: $__fusion_12_id) {
          weight
        }
      }
    source: $.productByIdForWeight
    target: $.product
    requirements:
      - name: __fusion_12_id
        selectionMap: >-
          id
    conditions:
      - variable: $a
        passingValue: false
      - variable: $b
        passingValue: false
    dependencies:
      - id: 1
  - id: 12
    type: Operation
    schema: b
    operation: |
      query Op_123456789101112_12($__fusion_14_weight: Int, $__fusion_15_id: ID!) {
        productById(id: $__fusion_15_id) {
          shippingCost(weight: $__fusion_14_weight)
        }
      }
    source: $.productById
    target: $.product
    requirements:
      - name: __fusion_14_weight
        selectionMap: >-
          weight
      - name: __fusion_15_id
        selectionMap: >-
          id
    conditions:
      - variable: $a
        passingValue: false
      - variable: $b
        passingValue: true
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
      - id: 13
  - id: 13
    type: Operation
    schema: c
    operation: |
      query Op_123456789101112_13($__fusion_16_id: ID!) {
        productByIdForWeight(id: $__fusion_16_id) {
          weight
        }
      }
    source: $.productByIdForWeight
    target: $.product
    requirements:
      - name: __fusion_16_id
        selectionMap: >-
          id
    conditions:
      - variable: $a
        passingValue: false
      - variable: $b
        passingValue: true
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
deliveryGroups:
  - id: 0
    path: $.product
    label: i
    ifVariable: $b
  - id: 1
    path: $.product
    label: o
    ifVariable: $a
  - id: 2
    path: $.product
    label: i
    ifVariable: $b
    parentId: 1

```

## nested argument requirements

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
            price(cur: "EUR") @fusion__requirement
            ... @defer(__group: 1, __parent: 0) {
              usd
              upc @fusion__requirement
              price(cur: "USD") @fusion__requirement
            }
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
  - id: 5
    type: Operation
    schema: b
    operation: |
      query Op_123456789101112_5($__fusion_5_upc: String!) {
        productByUpcB(upc: $__fusion_5_upc) {
          price(cur: "EUR")
        }
      }
    source: $.productByUpcB
    target: $.products
    batchingGroupId: 11
    requirements:
      - name: __fusion_5_upc
        selectionMap: >-
          upc
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
  - id: 9
    type: Operation
    schema: b
    operation: |
      query Op_123456789101112_9($__fusion_10_upc: String!) {
        productByUpcB(upc: $__fusion_10_upc) {
          price(cur: "USD")
        }
      }
    source: $.productByUpcB
    target: $.products
    batchingGroupId: 11
    requirements:
      - name: __fusion_10_upc
        selectionMap: >-
          upc
    deliveryGroups:
      - 1
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
    batchingGroupId: 12
    requirements:
      - name: __fusion_3_price
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
  - id: 8
    type: Operation
    schema: a
    operation: |
      query Op_123456789101112_8($__fusion_8_price: Int, $__fusion_9_upc: String!) {
        productByUpc(upc: $__fusion_9_upc) {
          usd(price: $__fusion_8_price)
        }
      }
    source: $.productByUpc
    target: $.products
    batchingGroupId: 12
    requirements:
      - name: __fusion_8_price
        selectionMap: >-
          price(cur: "USD")
      - name: __fusion_9_upc
        selectionMap: >-
          upc
    deliveryGroups:
      - 1
    dependencies:
      - id: 1
      - id: 9
deliveryGroups:
  - id: 0
    path: $.products
  - id: 1
    path: $.products
    parentId: 0

```
